using System;
using System.Net;
using System.Text;
using System.Linq;
using System.Text.Json;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections;
using Microsoft.VisualBasic;

namespace Big2Server{

    // 封包
    public enum PacketType { CreateRoom, UpdateRooms, LeaveRoom, JoinRoom, StartGame, GameSync, PlayCard }
    public class Packet { 
        public PacketType Type { get; set; } 
        public string? Data { get; set; } 
    }
    public class RoomState {
        public string? RoomName { get; set; }
        public string? OwnerName { get; set; }
        public List<string> PlayerNames { get; set; } = new();
        public int PlayerCount => PlayerNames.Count;
        public bool IsGameStarted { get; set; }
        public GameData? Game { get; set; }
    }

    // 卡牌
    public enum Suit { Club, Diamond, Heart, Spade } // ♣, ♦, ♥, ♠
    public class Card{
        public int Rank { get; set; }
        public Suit Suit { get; set; }
    }
    public class GameData{
        public List<List<Card>> PlayerHands { get; set; } = new(); // 4個人的手牌
        public List<Card> LastPlay { get; set; } = new(); // 桌面上最後出的牌
        public int CurrentTurn { get; set; } = 0; // 輪到誰 (0-3)
        public string LastPlayerName { get; set; } = ""; // 最後出牌的人
        public int PassCount { get; set; } = 0;

        public bool IsGameOver { get; set; } = false;
        public string WinnerName { get; set; } = "";
    }
    
    // 主程式
    class Program{
        static readonly List<TcpClient> Clients = new();
        static readonly List<RoomState> Rooms = new();
        static async Task Main(){
            TcpListener listener = new(IPAddress.Any, 12345);
            listener.Start();
            Console.WriteLine("伺服器啟動 (Port: 12345)...");

            while(true){
                TcpClient client = await listener.AcceptTcpClientAsync();
                Clients.Add(client);
                Console.WriteLine("新玩家連線！");
                _ = HandleClient(client);
            }
        }
        static async Task HandleClient(TcpClient client) {
            var stream = client.GetStream();
            byte[] buffer = new byte[8192];
            string? myName = null;

            await BroadcastRooms(); // 廣播給Client

            try{
                while(client.Connected){
                    // 讀封包 + 解封包
                    int read = await stream.ReadAsync(buffer);
                    if(read == 0) break;
                    string json = Encoding.UTF8.GetString(buffer, 0, read);
                    var packet = JsonSerializer.Deserialize<Packet>(json);
                    if (packet == null) continue;

                    // 判斷封包
                    switch(packet.Type){
                        case PacketType.CreateRoom:
                            var NewRoom = JsonSerializer.Deserialize<RoomState>(packet.Data!)!;
                            myName = NewRoom.OwnerName;
                            Rooms.Add(NewRoom);
                            await BroadcastRooms();
                            break;

                        case PacketType.JoinRoom:
                            var Player = JsonSerializer.Deserialize<JoinRequest>(packet.Data!)!;
                            myName = Player.PlayerName; 
                            var Room = Rooms.Find(r => r.RoomName == Player!.RoomName);

                            if(Room != null && Room.PlayerCount < 4){
                                Room.PlayerNames.Add(Player.PlayerName!);
                                Console.WriteLine($"{Player.PlayerName} 加入了房間 {Room.RoomName}");
                                await BroadcastRooms();
                            }
                            break;

                        case PacketType.LeaveRoom:
                            var leaveData = JsonSerializer.Deserialize<JoinRequest>(packet.Data!)!;
                            var TargetRoom = Rooms.Find(r => r.RoomName == leaveData.RoomName);

                            if(TargetRoom != null){
                                // 房主
                                if(TargetRoom.OwnerName == leaveData.PlayerName){
                                    Console.WriteLine($"房主 {leaveData.PlayerName} 離開，解散房間 {TargetRoom.RoomName}");
                                    Rooms.Remove(TargetRoom);
                                }
                                // 正常玩家
                                else{
                                    TargetRoom.PlayerNames.Remove(leaveData.PlayerName!);
                                    Console.WriteLine($"玩家 {leaveData.PlayerName} 離開了房間 {TargetRoom.RoomName}");
                                }
                                await BroadcastRooms();
                            }
                            break;

                        case PacketType.StartGame:
                            string RoomName = JsonSerializer.Deserialize<string>(packet.Data!)!;
                            var room = Rooms.Find(r => r.RoomName == RoomName);

                            if(room == null || room.PlayerNames.Count != 4) break;

                            room.IsGameStarted = true;
                            room.Game = new GameData();
                            var GameState = room.Game; // 房間的遊戲狀態

                            // 創卡牌
                            var deck = CreateDeck();

                            // 每個玩家給13張牌
                            for(int i = 0; i < 4; i++){
                                GameState.PlayerHands.Add(
                                    SortHand(deck.Skip(i*13).Take(13).ToList())
                                );
                            }
                            
                            // 梅花 3 開始
                            for(int i=0; i<4; i++){
                                if(GameState.PlayerHands[i].Any(
                                    c => c.Rank == 3 && c.Suit == Suit.Club
                                )){
                                    GameState.CurrentTurn = i;
                                    break;
                                }
                            }

                            Console.WriteLine($"房間 {RoomName} 遊戲正式開始！");
                            /*
                            foreach(var card in GameState.PlayerHands[GameState.CurrentTurn])
                                Console.WriteLine($"{card.Rank} {card.Suit}");
                            */
                            await BroadcastRooms();
                            break;

                        case PacketType.UpdateRooms:
                            await BroadcastRooms();
                            break;
                    
                        case PacketType.PlayCard:
                            
                            // 遊戲狀態
                            var gameData = JsonSerializer.Deserialize<GameData>(packet.Data!)!;
                            if (gameData == null) break;

                            // 找玩家所在房間
                            var targetRoom = Rooms.Find(r => r.PlayerNames.Contains(myName!));
                            if (targetRoom == null || targetRoom.Game == null) break;

                            var game = targetRoom.Game;
                            int playerIndex = targetRoom.PlayerNames.IndexOf(myName!);

                            if(gameData.LastPlay.Count == 0){ // 玩家 PASS
                                Console.WriteLine($"{gameData.LastPlayerName} 選擇 PASS");
                                game.PassCount++;
                                if(game.PassCount == 3){
                                    Console.WriteLine("三人 PASS，清桌");
                                    game.LastPlay.Clear();
                                    game.PassCount = 0;

                                    int lastIndex = targetRoom.PlayerNames.IndexOf(game.LastPlayerName);
                                    game.CurrentTurn = lastIndex;
                                }
                                else{
                                    game.CurrentTurn = (game.CurrentTurn + 1) % 4;
                                }
                            }
                            else{ // 玩家出牌
                                Console.WriteLine($"{gameData.LastPlayerName} 出牌 {string.Join(",", gameData.LastPlay.Select(c => c.Rank + c.Suit.ToString()))}");

                                // 從玩家手牌移除已出的牌
                                foreach(var c in gameData.LastPlay){
                                    game.PlayerHands[playerIndex].RemoveAll(card => card.Rank == c.Rank && card.Suit == c.Suit);
                                }

                                game.PlayerHands[playerIndex] = SortHand(game.PlayerHands[playerIndex]);

                                // 更新桌上最後出牌
                                game.LastPlay = gameData.LastPlay;
                                game.LastPlayerName = myName!;
                                game.PassCount = 0;
                                game.CurrentTurn = (game.CurrentTurn + 1) % 4;
                            }
                            if(game.PlayerHands[playerIndex].Count == 0){
                                Console.WriteLine($"{myName} 出完牌，遊戲結束");

                                game.IsGameOver = true;
                                game.WinnerName = myName!;
                                game.CurrentTurn = -1;     // 停止所有操作
                                game.LastPlay.Clear();     // 清桌
                            }
                            await BroadcastGame(targetRoom);
                            break;
                    }
                }
            }
            finally{ // 如果強制關視窗
                Clients.Remove(client);
                var Room = Rooms.Find(r => r.PlayerNames.Any(name => name == myName));
                if(Room != null){
                    
                    bool RemoveRoom = false;
                    if(Room.IsGameStarted){
                        // 遊戲中
                        Console.WriteLine($"遊戲中玩家 {myName} 斷開，強制解散房間 {Room.RoomName}");
                        RemoveRoom = true;
                    }
                    else{
                        // 準備聽
                        if(Room.OwnerName == myName){
                            Console.WriteLine($"房主 {myName} 離開，解散房間 {Room.RoomName}");
                            RemoveRoom = true;
                        }
                        else{
                            Console.WriteLine($"玩家 {myName} 離開，房間 {Room.RoomName} 繼續等待");
                            Room.PlayerNames.Remove(myName!);
                        }
                    }

                    // 遊戲中 任何人斷線都退回大廳
                    if(RemoveRoom) Rooms.Remove(Room);
                
                    await BroadcastRooms();
                }
                client.Close();
            }
        }
        static async Task BroadcastRooms(){
            var packet = new Packet{
                Type = PacketType.UpdateRooms,
                Data = JsonSerializer.Serialize(Rooms)
            };
            byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet));

            // 廣播給所有 client
            var tasks = Clients
                .Where (c => c.Connected)
                .Select(c => c.GetStream().WriteAsync(data).AsTask());
            await Task.WhenAll(tasks);
        }
        static async Task BroadcastGame(RoomState room){
            if(room.Game == null) return;

            var packet = new Packet{
                Type = PacketType.GameSync,
                Data = JsonSerializer.Serialize(room.Game)
            };
            byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet));

            // 廣播給房間內玩家
            var tasks = Clients
                        .Where(c => c.Connected)
                        .Select(c => c.GetStream().WriteAsync(data).AsTask());
            await Task.WhenAll(tasks);
        }
        public class JoinRequest{
            public string? RoomName { get; set; }
            public string? PlayerName { get; set; }
        }

        // 創卡牌
        static List<Card> CreateDeck(){
            var deck = new List<Card>();
            foreach(Suit s in Enum.GetValues<Suit>()){ // 4個
                for(int i=3; i<=15; i++){ // 12張
                    deck.Add(new(){ Rank = i, Suit = s});
                }
            }
            return deck.OrderBy(_ => Guid.NewGuid()).ToList();
        }

        // 排序卡牌
        static List<Card> SortHand(List<Card> hand){
            return hand
                .OrderBy(c => Big2RankValue(c.Rank))
                .ThenBy(c => Big2SuitValue(c.Suit))
                .ToList();
        }
        static int Big2RankValue(int rank){
            return rank 
            switch{
                3  => 1,
                4  => 2,
                5  => 3,
                6  => 4,
                7  => 5,
                8  => 6,
                9  => 7,
                10 => 8,
                11 => 9,   // J
                12 => 10,  // Q
                13 => 11,  // K
                14 => 12,  // A
                15 => 13,  // 2
                _  => 99
            };
        }
        static int Big2SuitValue(Suit suit){
            return suit
            switch{
                Suit.Club    => 1,
                Suit.Diamond => 2,
                Suit.Heart   => 3,
                Suit.Spade   => 4,
                _ => 99
            };
        }
    }
}