@echo off
rem 自動で遊ぶデモ
start "" "%LOCALAPPDATA%\Programs\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0." -- --demo
