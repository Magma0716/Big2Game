@echo off

start /min "Server" cmd /c "cd /d %~dp0Poker_Card_Server && dotnet run"
timeout /t 3
start /min "Player 1" cmd /c "cd /d %~dp0Poker_Card && dotnet run -- 0 2"
timeout /t 1
start /min "Player 2" cmd /c "cd /d %~dp0Poker_Card && dotnet run -- 960 2" 
timeout /t 1
start /min "Player 3" cmd /c "cd /d %~dp0Poker_Card && dotnet run -- 0 540"
timeout /t 1
start /min "Player 4" cmd /c "cd /d %~dp0Poker_Card && dotnet run -- 960 540"