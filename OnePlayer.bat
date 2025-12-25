@echo off

start /min "Server" cmd /c "cd /d %~dp0Poker_Card_Server && dotnet run"
timeout /t 2
start /min "Player N" cmd /c "cd /d %~dp0Poker_Card && dotnet run -- 100 100"
timeout /t 1