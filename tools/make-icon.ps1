# StreamRaid の仮アイコン(紫の盾に剣と王冠)を icon.png と icon.ico に書き出す
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot

function Draw([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # 角の丸い四角(背景)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 56 * $s; $m = 8 * $s; $w = $size - 2 * $m
    $path.AddArc($m, $m, $r, $r, 180, 90)
    $path.AddArc($m + $w - $r, $m, $r, $r, 270, 90)
    $path.AddArc($m + $w - $r, $m + $w - $r, $r, $r, 0, 90)
    $path.AddArc($m, $m + $w - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 0)), ([System.Drawing.PointF]::new($size, $size)), ([System.Drawing.Color]::FromArgb(255, 108, 59, 209)), ([System.Drawing.Color]::FromArgb(255, 255, 77, 141))
    $g.FillPath($bg, $path)

    # 剣(斜め)
    $g.TranslateTransform(128 * $s, 162 * $s)
    $g.RotateTransform(-35)
    $blade = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 240, 246, 255))
    $g.FillPolygon($blade, @(
        [System.Drawing.PointF]::new(-12 * $s, 26 * $s), [System.Drawing.PointF]::new(-12 * $s, -58 * $s),
        [System.Drawing.PointF]::new(0, -78 * $s), [System.Drawing.PointF]::new(12 * $s, -58 * $s), [System.Drawing.PointF]::new(12 * $s, 26 * $s)))
    $gold = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 211, 77))
    $g.FillRectangle($gold, -38 * $s, 26 * $s, 76 * $s, 14 * $s)
    $g.FillRectangle($gold, -9 * $s, 40 * $s, 18 * $s, 34 * $s)
    $g.ResetTransform()

    # 王冠(上)
    $g.FillPolygon($gold, @(
        [System.Drawing.PointF]::new(84 * $s, 70 * $s), [System.Drawing.PointF]::new(90 * $s, 34 * $s), [System.Drawing.PointF]::new(109 * $s, 54 * $s),
        [System.Drawing.PointF]::new(128 * $s, 26 * $s), [System.Drawing.PointF]::new(147 * $s, 54 * $s), [System.Drawing.PointF]::new(166 * $s, 34 * $s),
        [System.Drawing.PointF]::new(172 * $s, 70 * $s)))
    $g.Dispose()
    return $bmp
}

$big = Draw 256
$big.Save((Join-Path $root 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)

# .ico(PNG を中に入れる形式。16〜256 の 6 サイズ)
$sizes = 16, 32, 48, 64, 128, 256
$pngs = foreach ($sz in $sizes) {
    $b = Draw $sz
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    , $ms.ToArray()
}
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz }))); $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
[IO.File]::WriteAllBytes((Join-Path $root 'icon.ico'), $out.ToArray())
"icon.png / icon.ico を作りました"
