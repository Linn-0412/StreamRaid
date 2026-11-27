using System.Collections.Generic;
using System.Linq;
using Godot;

namespace StreamRaid;

/// <summary>
/// ゲーム全体の流れ: 参加受付 → ダンジョン(数フロア)→ ボスレイド → 結果発表。
/// コメント(ChatMsg)はすべて OnChat に届く。
/// </summary>
public partial class Main : Node3D
{
    public static Main I { get; private set; }
    public enum Phase { Lobby, Explore, Boss, Result }

    public readonly List<Unit> Units = new();
    public Settings Cfg { get; private set; }
    public bool InputBlocked => _settings != null && _settings.Open;
    public Player Player { get; private set; }
    public Hud Hud { get; private set; }
    public Node3D Level { get; private set; }
    public Phase State { get; private set; }
    public double Now { get; private set; }
    public Node3D Portal => _portal;
    public Boss BossUnit => _boss;
    public List<string> PartyNames => _party.Keys.ToList();

    Node3D _actors;
    CameraRig _cam;
    TestChat _chat;
    VrmAvatar _avatarCache;
    string _avatarCachePath, _avatarOverride;
    ChatHub _hub;
    SettingsPanel _settings;
    bool _demo;
    Viewers _viewers;
    readonly RandomNumberGenerator _rng = new();

    int _floor;
    float _lobbyLeft;
    int _waveLeft;
    Dungeon.Built _map;
    Node3D _portal;
    Boss _boss;
    double _resultAt = -1;
    bool _win;

    // パーティー(視聴者 → 職業)と、画面に出ている仲間
    readonly Dictionary<string, Job> _party = new();
    readonly Dictionary<string, Ally> _allies = new();
    readonly List<(string user, Job job)> _bench = new();
    readonly HashSet<string> _participants = new();
    readonly HashSet<string> _raiders = new();
    readonly Dictionary<string, float> _damage = new();
    readonly Dictionary<string, float> _sabotage = new();
    readonly Dictionary<string, double> _cooldown = new();

    public override void _Ready()
    {
        I = this;
        if (OS.GetCmdlineUserArgs().Contains("--avatartest"))
        {
            // 開発用: アバターの見た目と動きだけを確認する
            AddChild(new AvatarTest());
            SetProcess(false);
            Hud = null;
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--chattest"))
        {
            // 開発用: コメント取得だけを確認する
            AddChild(new ChatTest());
            SetProcess(false);
            Hud = null;
            return;
        }
        bool demo = _demo = OS.GetCmdlineUserArgs().Contains("--demo");
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--avatar=")) _avatarOverride = a["--avatar=".Length..]; // 開発用
        Cfg = demo ? new Settings() : Settings.Load();
        if (demo) { Cfg.LobbySeconds = 5; Cfg.FloorCount = 2; Seed(7); }
        else _rng.Randomize();
        Inputs.Setup();
        Toon.Init();
        SetupWorld();

        Level = new Node3D { Name = "Level" };
        AddChild(Level);
        _actors = new Node3D { Name = "Actors" };
        AddChild(_actors);
        _cam = new CameraRig();
        AddChild(_cam);
        Hud = new Hud();
        AddChild(Hud);
        _viewers = new Viewers(demo ? "demo" : Cfg.ChannelKey);
        _chat = new TestChat();
        AddChild(_chat);
        _chat.Message += OnChat;
        _hub = new ChatHub();
        AddChild(_hub);
        _hub.Message += OnChat;
        _settings = new SettingsPanel();
        _settings.Setup(Cfg, _hub);
        AddChild(_settings);
        _settings.Applied += OnSettingsApplied;
        // ダンジョン・ボス戦の途中で設定を開いたらゲームを一時停止する(参加受付中は受付時間だけ止める)
        _settings.Toggled += open => GetTree().Paused = open && State is Phase.Explore or Phase.Boss;
        if (demo) AddChild(new DemoBot { Chat = _chat });
        else
        {
            _hub.Apply(Cfg);
            // まだ接続先が無ければ最初に設定画面を出す
            if (!Cfg.HasConnection) CallDeferred(nameof(OpenSettings));
        }

        EnterLobby();
    }

    void OpenSettings() => _settings.Show(true);

    string AvatarPath => _avatarOverride ?? Cfg.AvatarPath;

    /// <summary>配信者のアバターを渡す(読み込みは 1 回だけ。作り直すときは使い回す)。</summary>
    public VrmAvatar TakeAvatar()
    {
        var path = AvatarPath;
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (_avatarCache != null && _avatarCachePath == path && GodotObject.IsInstanceValid(_avatarCache))
        {
            var c = _avatarCache;
            _avatarCache = null;
            return c;
        }
        var model = AvatarLoader.Load(path, out var err);
        if (model == null)
        {
            Hud?.Log($"アバターを読み込めませんでした: {err}", new Color("ff9a9a"));
            return null;
        }
        _avatarCachePath = path;
        return VrmAvatar.Create(model, 1.75f);
    }

    /// <summary>配信者を作り直す前に、アバターを外して取っておく。</summary>
    void KeepAvatar()
    {
        if (Player == null || !GodotObject.IsInstanceValid(Player)) return;
        var a = Player.ReleaseAvatar();
        if (a == null) return;
        _avatarCache?.QueueFree();
        _avatarCache = a;
    }

    void OnSettingsApplied(Settings s)
    {
        _hub.Apply(s);
        if (Player != null && IsInstanceValid(Player)) Player.Rename(s.StreamerName);
        // アバターが変わったら配信者を作り直す(同じ場所・同じ HP で)
        if (Player != null && IsInstanceValid(Player) && (s.AvatarPath ?? "") != (_avatarCachePath ?? "") && _avatarOverride == null)
        {
            var pos = Player.GlobalPosition;
            float hp = Player.Hp;
            Player.ReleaseAvatar()?.QueueFree();
            _avatarCache?.QueueFree();
            _avatarCache = null;
            _avatarCachePath = null;
            Player.QueueFree();
            Units.Remove(Player);
            SpawnPlayer(pos);
            Player.Hp = Mathf.Min(hp, Player.MaxHp);
            if (string.IsNullOrWhiteSpace(s.AvatarPath)) _avatarCachePath = "";
        }
        // 視聴者の記録は接続先(チャンネル)ごとに分ける
        _viewers.Save();
        _viewers = new Viewers(s.ChannelKey);
        Hud.Log("接続設定を保存しました", new Color("8ff0ff"));
    }

    void Seed(ulong s)
    {
        _rng.Seed = s;
        GD.Seed(s);
    }

    void SetupWorld()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color("1d1830"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("9a94c8"),
            AmbientLightEnergy = 0.55f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            GlowEnabled = true,
            GlowIntensity = 0.7f,
            GlowBloom = 0.08f,
        };
        AddChild(new WorldEnvironment { Environment = env });
        var sun = new DirectionalLight3D { ShadowEnabled = true, LightEnergy = 1.15f, LightColor = new Color("fff1d6") };
        AddChild(sun);
        sun.RotationDegrees = new Vector3(-58, -32, 0);
    }

    // ---------------------------------------------------------------- 流れ

    void EnterLobby()
    {
        State = Phase.Lobby;
        ClearAll();
        _floor = 0;
        _party.Clear(); _allies.Clear(); _bench.Clear();
        _participants.Clear(); _raiders.Clear();
        _damage.Clear(); _sabotage.Clear(); _cooldown.Clear();
        _boss = null;
        _resultAt = -1;
        _map = Dungeon.Build(Level, 18, 0, 0, _rng);
        SpawnPlayer(new Vector3(0, 0, 2));
        _lobbyLeft = Cfg.LobbySeconds;
        Hud.ShowResult(null, null);
        Hud.Banner("参加受付中!", 2.5f);
        Hud.SetHint("コメントで「#参加 戦士」「#参加 魔法使い」「#参加 僧侶」と書くと仲間になれます");
    }

    void StartRun()
    {
        State = Phase.Explore;
        Hud.Log("冒険に出発!", new Color("ffe9a8"));
        NextFloor();
    }

    void NextFloor()
    {
        _floor++;
        ClearMonstersAndLevel();
        if (_floor > Cfg.FloorCount) { EnterBoss(); return; }
        float size = 24 + _floor * 2;
        _map = Dungeon.Build(Level, size, 4 + _floor, _floor, _rng);
        PlaceHeroes(new Vector3(0, 0, _map.Half - 3));
        int total = 4 + _floor * 2;
        int first = total / 2 + 1;
        for (int i = 0; i < first; i++) SpawnMonster(RandomKind(), null, RandomSpot(8));
        _waveLeft = total - first;
        _portal = null;
        Hud.Banner($"地下 {_floor} 階", 1.8f);
        Hud.SetHint("「#召喚 スライム / ゴブリン / コウモリ」で敵を送れます。仲間は「技」でスキル");
    }

    void EnterBoss()
    {
        State = Phase.Boss;
        _map = Dungeon.Build(Level, 34, 0, 4, _rng);
        PlaceHeroes(new Vector3(0, 0, _map.Half - 4));
        _boss = new Boss();
        _actors.AddChild(_boss);
        _boss.GlobalPosition = new Vector3(0, 0, -5);
        _boss.MaxHp = _boss.Hp = 1400 + 120 * _participants.Count;
        _boss.Died += _ => Finish(true);
        _cam.Offset = new Vector3(0, 15, 13);
        Hud.Banner($"{Boss.BossName} 出現!", 2.5f, new Color("ff8f8f"));
        Hud.SetHint("コメントするとボスに攻撃が飛びます! 投げ銭は必殺技");
    }

    void Finish(bool win)
    {
        if (State == Phase.Result) return;
        _win = win;
        State = Phase.Result;
        Hud.Banner(win ? "討伐成功!" : "全滅…", 2f, win ? new Color("fff3a0") : new Color("ff8f8f"));
        _resultAt = Now + 2.2;
        foreach (var u in Units.ToArray()) if (u.Team == Team.Monster && u != _boss) u.QueueFree();
    }

    void ShowResult()
    {
        var lines = new List<(string, Color, int)>();
        var rank = _damage.Where(kv => kv.Key != null).OrderByDescending(kv => kv.Value).ToList();
        if (rank.Count > 0)
        {
            lines.Add(($"MVP  {rank[0].Key}  {Mathf.RoundToInt(rank[0].Value)} ダメージ", new Color("ffd34d"), 40));
            _viewers.Get(rank[0].Key).Mvp++;
            for (int i = 1; i < Mathf.Min(5, rank.Count); i++)
                lines.Add(($"{i + 1}位  {rank[i].Key}  {Mathf.RoundToInt(rank[i].Value)}", Colors.White, 30));
        }
        var sab = _sabotage.OrderByDescending(kv => kv.Value).FirstOrDefault();
        if (sab.Key != null)
            lines.Add(($"いちばん邪魔した人  {sab.Key}  ({Mathf.RoundToInt(sab.Value)} ダメージ)", new Color("ff9a9a"), 30));

        var ups = new List<string>();
        foreach (var name in _participants.Union(_raiders))
        {
            int exp = (_win ? 80 : 30) + Mathf.RoundToInt(_damage.GetValueOrDefault(name) / 15f);
            var v = _viewers.Get(name);
            v.Runs++;
            int up = _viewers.AddExp(name, exp);
            if (up > 0) ups.Add($"{name} Lv{v.Level}");
        }
        if (ups.Count > 0)
        {
            // 名前の途中で折り返さないよう、1 行 3 人ずつ並べる
            lines.Add(("レベルアップ!", new Color("8dff9a"), 30));
            var shown = ups.Take(6).ToList();
            for (int i = 0; i < shown.Count; i += 3)
            {
                var row = string.Join("   ", shown.Skip(i).Take(3));
                if (i + 3 >= shown.Count && ups.Count > 6) row += $"   ほか {ups.Count - 6} 人";
                lines.Add((row, new Color("8dff9a"), 26));
            }
        }
        lines.Add(($"参加 {_participants.Union(_raiders).Count()} 人", new Color("c9c3e6"), 26));
        lines.Add(("Enter でもう一度", new Color("c9c3e6"), 24));
        _viewers.Save();
        Hud.ShowResult(_win ? "討伐成功!" : "全滅…", lines);
        Hud.BossBar(null);
    }

    public override void _Process(double delta)
    {
        if (Hud == null) return;
        Now += delta;
        float dt = (float)delta;
        switch (State)
        {
            case Phase.Lobby:
                // 配信者が接続設定を開いている間は受付の残り時間を止める
                if (!InputBlocked) _lobbyLeft -= dt;
                Hud.SetTop(InputBlocked
                    ? $"参加受付中  あと {Mathf.CeilToInt(Mathf.Max(0, _lobbyLeft))} 秒(設定中は止まっています)"
                    : $"参加受付中  あと {Mathf.CeilToInt(Mathf.Max(0, _lobbyLeft))} 秒   (Enter で出発)");
                if (_lobbyLeft <= 0) StartRun();
                break;
            case Phase.Explore:
                int left = MonsterCount;
                Hud.SetTop($"地下 {_floor} / {Cfg.FloorCount} 階    敵 {left + _waveLeft}");
                if (left <= 1 && _waveLeft > 0)
                {
                    for (int i = 0; i < _waveLeft; i++) SpawnMonster(RandomKind(), null, RandomSpot(9));
                    _waveLeft = 0;
                    Hud.Log("敵の増援が来た!", new Color("ff9a9a"));
                }
                if (left == 0 && _waveLeft == 0 && _portal == null)
                {
                    _portal = Dungeon.Portal(Level, new Vector3(0, 0, -_map.Half + 3));
                    Hud.Banner("フロアクリア!", 1.4f);
                }
                if (_portal != null && Player != null && Unit.Flat(Player.GlobalPosition - _portal.GlobalPosition).Length() < 1.4f)
                    NextFloor();
                break;
            case Phase.Boss:
                Hud.SetTop($"ボス戦    参加 {_raiders.Count} 人");
                Hud.BossBar(_boss);
                break;
            case Phase.Result:
                Hud.SetTop("結果発表");
                if (_resultAt > 0 && Now >= _resultAt) { _resultAt = -1; ShowResult(); }
                break;
        }
        UpdatePartyText();
        Hud.SetStatus(_demo ? "デモ" : _hub.StatusLine() + "    F10 接続設定");
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Hud == null || !e.IsActionPressed("start")) return;
        if (State == Phase.Lobby) StartRun();
        else if (State == Phase.Result && _resultAt < 0) EnterLobby();
    }

    // ---------------------------------------------------------------- コメント

    void OnChat(ChatMsg m)
    {
        var text = (m.Text ?? "").Trim();
        if (string.IsNullOrEmpty(m.User)) return;
        // 配信者本人のコメントは仲間・召喚にしない(配信者はプレイヤーなので)
        if (TryCommand(text, "#参加", "!join", out var arg)) { if (!m.Owner) Join(m.User, ParseJob(arg)); return; }
        if (TryCommand(text, "#召喚", "!summon", out arg)) { if (!m.Owner) Summon(m.User, ParseKind(arg)); return; }
        if (text is "技" or "#技" or "!skill") { Skill(m.User); if (State != Phase.Boss) return; }
        if (State == Phase.Boss) RaidHit(m);
    }

    static bool TryCommand(string text, string ja, string en, out string arg)
    {
        foreach (var p in new[] { ja, en })
            if (text.StartsWith(p, System.StringComparison.OrdinalIgnoreCase))
            {
                arg = text[p.Length..].Trim();
                return true;
            }
        arg = null;
        return false;
    }

    static Job ParseJob(string a) => a switch
    {
        _ when a.Contains("魔") || a.StartsWith("mage", System.StringComparison.OrdinalIgnoreCase) => Job.Mage,
        _ when a.Contains("僧") || a.Contains("回復") || a.StartsWith("priest", System.StringComparison.OrdinalIgnoreCase) || a.StartsWith("heal", System.StringComparison.OrdinalIgnoreCase) => Job.Priest,
        _ => Job.Warrior,
    };

    static Kind ParseKind(string a) => a switch
    {
        _ when a.Contains("ゴブ") || a.StartsWith("gob", System.StringComparison.OrdinalIgnoreCase) => Kind.Goblin,
        _ when a.Contains("コウモリ") || a.Contains("蝙蝠") || a.StartsWith("bat", System.StringComparison.OrdinalIgnoreCase) => Kind.Bat,
        _ => Kind.Slime,
    };

    bool OffCooldown(string user, string what, double seconds)
    {
        var key = user + "\n" + what;
        if (_cooldown.TryGetValue(key, out var t) && Now < t) return false;
        _cooldown[key] = Now + seconds;
        return true;
    }

    void Join(string user, Job job)
    {
        if (State == Phase.Result) return;
        if (_party.ContainsKey(user) || _bench.Any(b => b.user == user)) return;
        _participants.Add(user);
        var v = _viewers.Get(user);
        v.Job = job.ToString();
        if (_party.Count >= Cfg.PartySize)
        {
            _bench.Add((user, job));
            Hud.Log($"{user} が控えに入った({Ally.JobName(job)})", new Color("c9c3e6"));
            return;
        }
        _party[user] = job;
        SpawnAlly(user, job, Player != null ? Player.GlobalPosition + new Vector3(_rng.RandfRange(-2, 2), 0, 2) : Vector3.Zero);
        Hud.Log($"{user} が{Ally.JobName(job)}として参加! (Lv{v.Level})", Ally.JobColor(job));
    }

    void Summon(string user, Kind kind)
    {
        if (State != Phase.Explore) return;
        if (!OffCooldown(user, "summon", Cfg.SummonCooldown)) return;
        if (MonsterCount > 24) return;
        _participants.Add(user);
        SpawnMonster(kind, user, RandomSpot(7));
        Hud.Log($"{user} が{Enemy.KindName(kind)}を召喚した!", new Color("ff9a9a"));
    }

    void Skill(string user)
    {
        if (_allies.TryGetValue(user, out var a) && IsInstanceValid(a) && !a.Dead) a.Command();
    }

    void RaidHit(ChatMsg m)
    {
        if (_boss == null || !IsInstanceValid(_boss) || _boss.Dead) return;
        if (m.PaidYen <= 0 && !OffCooldown(m.User, "raid", Cfg.RaidInterval)) return;
        if (_raiders.Add(m.User) && _raiders.Count > _participants.Count) _boss.AddParticipant(60);

        float dmg = 12 + _rng.Randf() * 8;
        var color = new Color("fff6d6");
        float size = 0.22f;
        string label = m.User;
        if (m.Member) { dmg *= 1.4f; color = new Color("8dff9a"); }
        if (_party.TryGetValue(m.User, out var job))
        {
            dmg *= 1.5f;
            color = Ally.JobColor(job);
            size = 0.3f;
        }
        if (m.PaidYen > 0)
        {
            dmg = Mathf.Min(1500, 120 + m.PaidYen / 8f);
            color = new Color("ffd34d");
            size = 0.55f + Mathf.Min(0.6f, m.PaidYen / 10000f);
            label = $"{m.User}  {m.PaidText}";
            Hud.Banner($"{m.User} の必殺技!", 1.2f, new Color("ffd34d"));
        }
        var boss = _boss;
        float a = _rng.Randf() * Mathf.Tau;
        var from = boss.GlobalPosition + new Vector3(Mathf.Cos(a) * 11, 9 + _rng.Randf() * 3, Mathf.Sin(a) * 11);
        bool big = m.PaidYen > 0;
        var user = m.User;
        Fx.Shot(from, () => IsInstanceValid(boss) ? boss.GlobalPosition + Vector3.Up * 3 : from, big ? 1.0f : 0.75f, color, size, 2.5f, () =>
        {
            if (!IsInstanceValid(boss) || boss.Dead) return;
            boss.Damage(dmg, user);
            if (big) { Fx.Burst(boss.GlobalPosition + Vector3.Up * 3, color, 4.5f, 0.45f); Shake(0.7f); }
        }, label, color.Lightened(0.3f));
    }

    // ---------------------------------------------------------------- 生成・片付け

    void SpawnPlayer(Vector3 pos)
    {
        Player = new Player();
        _actors.AddChild(Player);
        Player.GlobalPosition = pos;
        Player.Died += _ => Finish(false);
        _cam.Target = Player;
        _cam.Offset = new Vector3(0, 11, 9.5f);
        _cam.Snap();
    }

    void SpawnAlly(string user, Job job, Vector3 pos)
    {
        var a = new Ally();
        a.Setup(user, job, _viewers.Get(user).Level);
        _actors.AddChild(a);
        a.GlobalPosition = pos;
        _allies[user] = a;
        a.Died += _ =>
        {
            Hud.Log(State == Phase.Boss ? $"{user} が倒れた…" : $"{user} が倒れた…(次の階で復活)", new Color("c9c3e6"));
            // 控えがいれば交代する
            if (_bench.Count > 0 && State == Phase.Explore)
            {
                var (bu, bj) = _bench[0];
                _bench.RemoveAt(0);
                _party.Remove(user);
                _allies.Remove(user);
                _bench.Add((user, job));
                _party[bu] = bj;
                SpawnAlly(bu, bj, Player.GlobalPosition + Vector3.Back * 2);
                Hud.Log($"{bu} が交代で参加!", Ally.JobColor(bj));
            }
        };
    }

    public Enemy SpawnMonster(Kind kind, string summoner, Vector3 pos)
    {
        var e = new Enemy();
        e.Setup(kind, summoner, summoner != null ? 1.15f : 1f);
        _actors.AddChild(e);
        e.GlobalPosition = pos;
        return e;
    }

    void PlaceHeroes(Vector3 pos)
    {
        if (Player != null && IsInstanceValid(Player))
        {
            Player.GlobalPosition = pos;
            Player.Revive(0.3f);
        }
        foreach (var (user, job) in _party.ToList())
        {
            var p = pos + new Vector3(_rng.RandfRange(-2.5f, 2.5f), 0, _rng.RandfRange(0.5f, 2f));
            if (_allies.TryGetValue(user, out var a) && IsInstanceValid(a) && !a.Dead)
            {
                a.GlobalPosition = p;
                a.Heal(a.MaxHp * 0.3f);
            }
            else SpawnAlly(user, job, p);
        }
        _cam.Snap();
    }

    Vector3 RandomSpot(float awayFromPlayer)
    {
        float h = _map.Half - 1.5f;
        for (int i = 0; i < 60; i++)
        {
            var p = new Vector3(_rng.RandfRange(-h, h), 0, _rng.RandfRange(-h, h));
            if (_map.Blocked.Any(b => b.DistanceTo(p) < 1.8f)) continue;
            if (Player != null && IsInstanceValid(Player) && Unit.Flat(Player.GlobalPosition - p).Length() < awayFromPlayer) continue;
            return p;
        }
        return new Vector3(0, 0, -h);
    }

    Kind RandomKind() => (Kind)_rng.RandiRange(0, _floor >= 2 ? 2 : 1);

    int MonsterCount => Units.Count(u => u.Team == Team.Monster && !u.Dead);

    void ClearMonstersAndLevel()
    {
        foreach (var u in Units.ToArray()) if (u.Team == Team.Monster) u.QueueFree();
        foreach (var c in Level.GetChildren()) c.QueueFree();
        Fx.Clear();
        _portal = null;
    }

    void ClearAll()
    {
        KeepAvatar();
        foreach (var c in _actors.GetChildren()) c.QueueFree();
        foreach (var c in Level.GetChildren()) c.QueueFree();
        Units.Clear();
        Fx.Clear();
        Player = null;
        _portal = null;
    }

    // ---------------------------------------------------------------- 集計

    /// <summary>与えたダメージを視聴者ごとに記録(結果発表の MVP 用)。</summary>
    public void Credit(string source, float amount, Unit target)
    {
        if (source == null || target.Team != Team.Monster) return;
        _damage[source] = _damage.GetValueOrDefault(source) + amount;
    }

    public void CreditSabotage(string summoner, float amount)
    {
        if (amount <= 0) return;
        _sabotage[summoner] = _sabotage.GetValueOrDefault(summoner) + amount;
    }

    public void Shake(float amount) => _cam.Shake(amount);

    void UpdatePartyText()
    {
        var sb = new System.Text.StringBuilder();
        if (Player != null && IsInstanceValid(Player))
            sb.AppendLine($"{Player.DisplayName}  HP {Mathf.CeilToInt(Player.Hp)}/{Mathf.CeilToInt(Player.MaxHp)}");
        foreach (var (user, job) in _party)
        {
            _allies.TryGetValue(user, out var a);
            bool alive = a != null && IsInstanceValid(a) && !a.Dead;
            string charge = alive && a.Charge >= 1 ? "  ★技OK" : "";
            sb.AppendLine(alive ? $"{Ally.JobName(job)}  {user}  Lv{a.Level}  {Mathf.CeilToInt(a.Hp / a.MaxHp * 100)}%{charge}"
                                : $"{Ally.JobName(job)}  {user}  (倒れた)");
        }
        if (_bench.Count > 0) sb.AppendLine($"控え {_bench.Count} 人");
        Hud.SetParty(sb.ToString());
    }
}
