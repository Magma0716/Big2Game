using System;
using Raylib_cs;
using System.Linq;
using System.Text;
using System.Numerics;
using System.Text.Json;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection.Metadata;

namespace Big2Game{
    
    // 伺服器
    public enum PacketType { CreateRoom, UpdateRooms, LeaveRoom, JoinRoom, StartGame, GameSync, PlayCard }
    public class Packet{
        public PacketType Type { get; set; }
        public string? Data { get; set; } // Json
    }
    public class RoomState{
        public string RoomName { get; set; } = ""; //房間名稱
        public string OwnerName { get; set; } = ""; //房主 
        public List<string> PlayerNames { get; set; } = new(); //玩家清單
        public int PlayerCount => PlayerNames.Count; //玩家人數
        public bool IsGameStarted { get; set; } //開始了嗎
        public GameData? Game { get; set; }

        public RoomState(string RoomName, string OwnerName, List<string> PlayerNames, bool IsGameStarted){
            this.RoomName = RoomName;
            this.OwnerName = OwnerName;
            this.PlayerNames = PlayerNames;
            this.IsGameStarted = IsGameStarted;
        }
    }
    public class NetworkClient{
        /* 
            Connerct:      Client -> Server -> Stream(Data)
            Receive:       Stream(Data)-> Buffer -> Read -> json
            ProcessPacket: json -> 全部自用
            SendPacket:    class -> packet -> json
        */ 
        private readonly TcpClient Client = new();
        public bool Connected => Client.Connected;
        private NetworkStream? Stream;
        private readonly byte[] Buffer = new byte[8192];
        public List<RoomState> Rooms { get; set; } = new();
        public bool KickToLobby { get; set; } = false;
        public async Task<bool> Connect(string ip, int port){
            try{
                await Client.ConnectAsync(ip, port);
                Stream = Client.GetStream();
                
                // 一直接收資料
                _ = Task.Run(Receive);
                return true;
            }
            catch{
                return false;
            }
        }
        private async Task Receive(){
            // 當連到 Server
            while(Client.Connected){
                int read = await Stream!.ReadAsync(Buffer);
                if(read > 0){
                    ProcessPacket(Encoding.UTF8.GetString(Buffer, 0, read)); // 丟給ProcessPacket
                }
            }
        }
        private void ProcessPacket(string json){
            var packet = JsonSerializer.Deserialize<Packet>(json);
            if(packet == null) return;
            /*
                {
                  "Type": "UpdateRooms",
                  "Data": "..."
                }
            */
            
            // 判斷封包類型
            switch(packet.Type){
                case PacketType.CreateRoom: // 創建房間
                    Console.WriteLine("房間建立成功");
                    break;

                case PacketType.UpdateRooms: // 更新房間
                    Rooms = JsonSerializer.Deserialize<List<RoomState>>(packet.Data!) ?? Rooms;
                    Console.WriteLine($"房間列表已更新，目前有 {Rooms.Count} 個房間");
                    break;

                case PacketType.LeaveRoom:
                    //KickToLobby = true;
                    break;

                case PacketType.JoinRoom: // 加入房間
                    //CurrentScene = 1;
                    break;

                case PacketType.StartGame: // 開始遊戲
                    //CurrentScene = 3;
                    break;
                
                case PacketType.GameSync: // 遊戲狀態更新
                    var gameData = JsonSerializer.Deserialize<GameData>(packet.Data!);
                    if (gameData == null) break;

                    // 找玩家所在房間
                    var room = Rooms.FirstOrDefault(r => r.PlayerNames.Contains(gameData.LastPlayerName));
                    if (room != null)
                    {
                        room.Game = gameData; // 更新遊戲狀態
                    }
                    break;
                
            }
        }
        public void SendPacket(PacketType type, object payload){
            if(!Client.Connected) return;
        
            Packet packet = new(){
                Type = type,
                Data = JsonSerializer.Serialize(payload) // Json
            };
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet));
            Stream!.Write(bytes, 0, bytes.Length);
        }
    }
    
    // 牌
    public enum Suit { Club, Diamond, Heart, Spade } // ♣, ♦, ♥, ♠
    enum HandType{
    Invalid,
    Single,
    Pair,
    Triple,
    Straight,
    FullHouse,
    FourKind
}

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

    public class Program{
        // 字體
        static Font font;
        static Font font2;
        // 計時器
        static float Timer = 0.0f;
        static float TimerInterval = 0.0f;
        // 選取的牌
        static List<Card> SelectedCards = new();
        static void Main(string[] args){
            // 視窗
            Raylib.InitWindow(960, 540, "大老二");
            Raylib.SetTargetFPS(60);

            // 圖片
            Image icon = Raylib.LoadImage(@".\images\Icon.png");
            Raylib.SetWindowIcon(icon);
            
            // 視窗位置 (bat用)
            if(args.Length >= 2 && int.TryParse(args[0], out int x) && int.TryParse(args[1], out int y)){
                Raylib.SetWindowPosition(x, y);
            }

            // 連接 Server
            NetworkClient client = new();
            Task.Run(async() => { //非同步連線
                _ = client.Connect("127.0.0.1", 12345);
            });

            // 字體
            string En = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()_+-=[]{}|;':\",./<>?♣♦♥♠ ";
            string Ch = "大老二玩家創建立遊戲房間名稱列表重新整理加入等待中央出牌區遊戲中主滿人即可離開始現在輪到你出牌";
            List<int> TextList = (En + Ch).Select(c => (int)c).Distinct().ToList();
            font = Raylib.LoadFontEx (@".\fonts\Cubic11.ttf", 48, TextList.ToArray(), TextList.Count);
            font2 = Raylib.LoadFontEx(@"C:\Windows\Fonts\seguisym.ttf", 48, TextList.ToArray(), TextList.Count);
            Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);
            Raylib.SetTextureFilter(font2.Texture, TextureFilter.Bilinear);

            // 圖片
            Texture2D LobbyBG = Raylib.LoadTexture(@".\images\LobbyBG.jpg");

            // 房間清單
            // RoomName, OwnerName, PlayerNames<>, PlayerCount, IsGameStarted
            List<RoomState> Rooms = new();
            
            // 輸入變數
            string PlayerName = "";
            string RoomName = "";
            int ButtonStates = 0; // 0:沒有, 1:創建房間, 2:重新整理, 3:玩家名稱, 4:房間名稱
            int CurrentScene = 1; // 0:沒有, 1:大廳 ,2:準備聽, 3:遊戲聽
            float ScrollOffset = 0;
            

            //視窗出現
            while(!Raylib.WindowShouldClose()){
                
                // 從client更新房間狀態
                Rooms = client.Rooms;

                // 滑鼠狀態
                Vector2 MousePos = Raylib.GetMousePosition();
                bool IsClick = Raylib.IsMouseButtonPressed(MouseButton.Left);

                // 畫背景
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.Black);
                Raylib.DrawTexturePro(
                    LobbyBG, 
                    new(0, 0, LobbyBG.Width, LobbyBG.Height),
                    new(0, 0, 960, 540), 
                    new(0, 0), 0, Color.White
                );

                // 輸入框
                Rectangle PlayerBox = new(50, 100, 300, 50);
                Rectangle RoomBox = new(50, 220, 300, 50);

                // 按鈕
                Rectangle CreateButton = new(50, 320, 300, 60);
                Rectangle RefreshButton = new(800, 50, 100, 40);

                // (玩家名稱, 房間名稱) 不能是空的
                bool CanCreate = !string.IsNullOrWhiteSpace(PlayerName) && !string.IsNullOrWhiteSpace(RoomName);

                // ====================
                // 判斷滑鼠按鈕狀態
                // ====================
                if(IsClick){
                    // 0:沒有, 1:創建房間, 2:重新整理, 3:玩家名稱, 4:房間名稱
                    if     (Raylib.CheckCollisionPointRec(MousePos, CreateButton))  ButtonStates = 1;
                    else if(Raylib.CheckCollisionPointRec(MousePos, RefreshButton)) ButtonStates = 2;
                    else if(Raylib.CheckCollisionPointRec(MousePos, PlayerBox))     ButtonStates = 3;
                    else if(Raylib.CheckCollisionPointRec(MousePos, RoomBox))       ButtonStates = 4;
                    else   ButtonStates = 0;
                }

                if     (CurrentScene == 1){ // (1)大廳
                    if(IsClick && CanCreate && ButtonStates == 1){  // 1.創建房間           
                        // RoomName, OwnerName, PlayerNames<>, PlayerCount, IsGameStarted
                        client.SendPacket(
                            PacketType.CreateRoom, 
                            new RoomState(RoomName!, PlayerName!, new(){PlayerName!}, false)
                        );
                        CurrentScene = 2;
                        RoomName = "";
                    }
                    if(IsClick && ButtonStates == 2){ // 2.重新整理
                        client.SendPacket(PacketType.UpdateRooms, "");
                        Console.WriteLine("重新整理列表...");
                    }
                    if(ButtonStates == 3) Input(ref PlayerName); // 3.玩家名稱
                    if(ButtonStates == 4) Input(ref RoomName);   // 4.房間名稱

                    // ====================
                    // 畫左側面板
                    // ====================
                    Raylib.DrawTextEx(font, "建立遊戲", new(50, 20), 40, 2, Color.White);
                    
                    // 玩家名稱 輸入框
                    DrawLabel("玩家名稱 (Player Name):", 50, 75);
                    DrawBox(PlayerBox, PlayerName!, ButtonStates == 3);

                    // 房間名稱 輸入框
                    DrawLabel("房間名稱 (Room Name):", 50, 195);
                    DrawBox(RoomBox, RoomName!, ButtonStates == 4);

                    // 創建房間 按鈕
                    bool HoverCreate = Raylib.CheckCollisionPointRec(MousePos, CreateButton);
                    Color CreateButtonColor;
                    if(CanCreate){
                        CreateButtonColor = HoverCreate ? Color.SkyBlue : Color.Blue;
                    }
                    else{
                        CreateButtonColor = Color.Gray;
                    }
                    Raylib.DrawRectangleRec(CreateButton, CreateButtonColor);
                    Raylib.DrawTextEx(font, "創建房間", new(CreateButton.X + 100, CreateButton.Y + 20), 24, 2, Color.White);
                    
                    // ====================
                    // 畫右側面板
                    // ====================
                    int listX = 400, listY = 100;
                    int listW = 500, listH = 380;

                    // 滾動輸入
                    float Wheel = Raylib.GetMouseWheelMove();
                    if(Raylib.CheckCollisionPointRec(MousePos, new(listX, listY, listW, listH))){
                        ScrollOffset += Wheel*20;
                    }

                    // 限制捲動範圍
                    float ListHeight = Rooms.Count*60+20;
                    if(ScrollOffset > 0){
                        ScrollOffset = 0;
                    }
                    if(ListHeight > listH){
                        if(ScrollOffset < -(ListHeight - listH)){
                            ScrollOffset = -(ListHeight - listH);
                        }
                    }
                    else{
                        ScrollOffset = 0;
                    }

                    // 房間列表 文字
                    Raylib.DrawTextEx(font, "房間列表", new(listX, 55), 30, 2, Color.Gold);
                    
                    // 重新整理 按鈕
                    bool HoverRefresh = Raylib.CheckCollisionPointRec(MousePos, RefreshButton);
                    Raylib.DrawRectangleRec(RefreshButton, HoverRefresh ? Color.DarkGreen : Color.Green);
                    Raylib.DrawTextEx(font, "重新整理", new((int)RefreshButton.X + 10, (int)RefreshButton.Y + 10), 20, 2, Color.Black);

                    // 列表背景
                    Raylib.DrawRectangle(listX, listY, listW, listH, new(50, 50, 60, 255));
                    Raylib.DrawRectangleLines(listX, listY, listW, listH, Color.Gray);

                    // 畫列表
                    Raylib.BeginScissorMode(listX, listY, listW, listH); // 滾動列表(開始)
                    for(int i = 0; i < Rooms.Count; i++){
                        var Room = Rooms[i];
                        int itemY = listY + 10 + (i*60) + (int)ScrollOffset;
                        
                        // 背景
                        Rectangle ListBG = new(listX+10, itemY, listW-20, 50);
                        if (itemY+50 < listY || itemY > listY + listH) continue; // 超出的不畫
                        Raylib.DrawRectangleRec(ListBG, Color.RayWhite);
                        
                        // 房間名稱
                        Raylib.DrawTextEx(font, Room.RoomName, new((int)ListBG.X+10, (int)ListBG.Y+14), 24, 2, Color.Black);
                        
                        // 人數 + 狀態
                        string Status;
                        Color StatusColor = Room.IsGameStarted ? Color.Red : Color.DarkGreen;
                        if(Room.IsGameStarted && Room.PlayerCount == 4){
                            Status = "遊戲中"; 
                            StatusColor = Color.Red;
                        }
                        else if(!Room.IsGameStarted && Room.PlayerCount == 4){
                            Status = "滿人 (4/4)"; 
                            StatusColor = Color.Red;
                        }
                        else{
                            Status = $"等待中 ({Room.PlayerCount}/4)";
                            StatusColor = Color.DarkGreen;
                        }
                        Raylib.DrawTextEx(font, Status, new((int)ListBG.X+250, (int)ListBG.Y+16), 20, 2, StatusColor);

                        // 5.加入按鈕
                        if(!Room.IsGameStarted && Room.PlayerCount < 4){
                            Rectangle JoinButton = new(ListBG.X+400, ListBG.Y+5, 60, 40);

                            // 狀態
                            bool hoverJoin = Raylib.CheckCollisionPointRec(MousePos, JoinButton);
                            Color TextColor;
                            
                            // 是否輸入玩家名字
                            if(!string.IsNullOrWhiteSpace(PlayerName)){
                                
                                Raylib.DrawRectangleRec(JoinButton, hoverJoin ? Color.Orange : Color.Gold);
                                TextColor = Color.Black;

                                if(hoverJoin && IsClick){
                                    if(!string.IsNullOrWhiteSpace(PlayerName)){
                                        client.SendPacket(
                                            PacketType.JoinRoom, 
                                            new { RoomName = Room.RoomName, PlayerName }
                                        );
                                        CurrentScene = 2;
                                    }
                                }
                            }
                            else{
                                Raylib.DrawRectangleRec(JoinButton, Color.Gray);
                                TextColor = Color.White;
                            }
                            
                            Raylib.DrawTextEx(font, "加入", new((int)JoinButton.X+10, (int)JoinButton.Y+10), 20, 2, TextColor);   
                        }
                    }
                    Raylib.EndScissorMode(); // 滾動列表(結束)
                    
                }
                else if(CurrentScene == 2){ // (2)準備庭
                    
                    var Room = Rooms.FirstOrDefault(r => r.PlayerNames.Contains(PlayerName));
                    
                    if(Room == null){ // 找不到房間
                        CurrentScene = 1;
                        RoomName = "";
                        continue;
                    }
                    if(client.KickToLobby){ // 是否被踢出
                        client.KickToLobby = false;
                        CurrentScene = 1;
                        continue;
                    }

                    // 背景
                    Rectangle mainBox = new(100, 50, 760, 440);
                    Raylib.DrawRectangleRec(mainBox, new(30, 30, 35, 200));
                    Raylib.DrawRectangleLinesEx(mainBox, 3, Color.Gold);

                    // 十字線
                    Raylib.DrawLineEx(new(100, 270), new(860, 270), 2, Color.Gray); // 橫線
                    Raylib.DrawLineEx(new(480, 50), new(480, 490), 2, Color.Gray);  // 直線

                    // 四個玩家位置
                    Vector2[] positions = {
                        new(120, 60),  // 左上 (P1)
                        new(500, 60),  // 右上 (P2)
                        new(120, 280), // 左下 (P3)
                        new(500, 280)  // 右下 (P4)
                    };

                    // 顯示 4 玩家資訊
                    for(int i = 0; i < 4; i++){
                        string playerName = (i < Room.PlayerCount)? Room.PlayerNames[i] : "等待中...";
                        Color nameColor = (i < Room.PlayerCount)? Color.White : Color.Gray;
                        
                        // 畫玩家名字
                        Raylib.DrawTextEx(font, $"P{i + 1}: {playerName}", positions[i], 30, 2, nameColor);

                        // 房主標註
                        if(i < Room.PlayerCount && Room.PlayerNames[i] == Room.OwnerName){
                            Raylib.DrawTextEx(font, "(房主)", new(positions[i].X + 150, positions[i].Y), 20, 2, Color.Yellow);
                            Raylib.DrawTriangle(
                                new(positions[i].X + 160, positions[i].Y - 10), 
                                new(positions[i].X + 150, positions[i].Y - 25), 
                                new(positions[i].X + 170, positions[i].Y - 25), Color.Yellow
                            );
                        }
                    }

                    // 6.離開按鈕
                    Rectangle LeaveButton = new(730, 10, 100, 40);
                    bool hoverLeave = Raylib.CheckCollisionPointRec(MousePos, LeaveButton);
                    Raylib.DrawRectangleRec(LeaveButton, hoverLeave ? Color.Maroon : Color.Red);
                    Raylib.DrawTextEx(font, "離開", new(LeaveButton.X + 31, LeaveButton.Y + 11), 20, 2, Color.White);

                    if(hoverLeave && IsClick){
                        client.SendPacket(
                            PacketType.LeaveRoom, 
                            new { RoomName = Room.RoomName, PlayerName = PlayerName }
                        );   
                        CurrentScene = 1;
                    }

                    // 房主
                    if(PlayerName == Room.OwnerName){
                        if(Room.PlayerCount == 4){ // 7.開始按鈕
                            Rectangle StartButton = new(380, 480, 200, 50);
                            bool hoverStart = Raylib.CheckCollisionPointRec(MousePos, StartButton);
                            Raylib.DrawRectangleRec(StartButton, hoverStart ? Color.Lime : Color.Green);
                            Raylib.DrawTextEx(font, "開始遊戲", new(StartButton.X + 50, StartButton.Y + 10), 24, 2, Color.White);
                            
                            if(hoverStart && IsClick){
                                client.SendPacket(PacketType.StartGame, Room.RoomName);
                            }
                        }
                        else{
                            Raylib.DrawTextEx(font, "等待玩家滿人即可開始...", new(380, 500), 20, 2, Color.SkyBlue);
                        }
                    }
                    // 正常玩家
                    else{
                        Raylib.DrawTextEx(font, "等待房主開始遊戲...", new(380, 500), 20, 2, Color.White);
                    }

                    // 遊戲開始
                    if(Room.IsGameStarted) {
                        Console.WriteLine("遊戲開始！進入遊戲畫面");
                        CurrentScene = 3;
                    }

                    Raylib.DrawTextEx(font, $"房間名稱: {Room.RoomName}", new(120, 20), 24, 2, Color.Gold);
                }
                else if(CurrentScene == 3){ // (3)遊戲聽
                    var Room = Rooms.FirstOrDefault(r => r.PlayerNames.Contains(PlayerName));
                    if(Room == null || Room?.Game == null){ // 找不到房間
                        CurrentScene = 1; 
                        continue; 
                    }
                    
                    var Game = Room.Game;
                    if(Game.IsGameOver){ // 遊戲結束
                        Raylib.DrawRectangle(0, 0, 960, 540, new Color(0, 0, 0, 180));
                        Raylib.DrawTextEx(
                            font,
                            $"遊戲結束！\n勝利者：{Game.WinnerName}",
                            new Vector2(300, 220),
                            36,
                            2,
                            Color.Gold
                        );

                        Raylib.DrawTextEx(
                            font,
                            "點擊滑鼠返回大廳",
                            new Vector2(360, 300),
                            22,
                            2,
                            Color.White
                        );

                        if(Raylib.IsMouseButtonPressed(MouseButton.Left)){
                            CurrentScene = 1;
                        }

                        Raylib.EndDrawing();
                        continue;
                    }
                    // 玩家資料
                    int MyIndex = Room.PlayerNames.IndexOf(PlayerName);
                    var MyHand = Game.PlayerHands[MyIndex];
                    
                    // 畫背景
                    Raylib.DrawRectangle(0, 0, 960, 540, new Color(0, 80, 0, 255));
                    if(Game.CurrentTurn == MyIndex)
                        Raylib.DrawRectangleLinesEx(new(10, 10, 940, 520), 5, new Color(255, 230, 110, 255));
                    else
                        Raylib.DrawRectangleLinesEx(new(10, 10, 940, 520), 5, Color.DarkGreen);

                    // 畫資訊
                    for(int i = 0; i < 4; i++){
                        if(i == MyIndex) continue; // 跳過自己

                        // 玩家位置
                        Vector2 PlayerPos = ((i - MyIndex + 4) % 4) 
                        switch{
                            1 => new Vector2(800, 200), // 右
                            2 => new Vector2(420, 50),  // 上
                            3 => new Vector2(50, 200),  // 左
                            _ => Vector2.Zero // 自己
                        };

                        // 對手資訊
                        Raylib.DrawRectangleV(PlayerPos, new(120, 80), new(0, 0, 0, 100));
                        Raylib.DrawTextEx(font, Room.PlayerNames[i], PlayerPos + new Vector2(10, 10), 20, 1, Color.White);
                        Raylib.DrawTextEx(font, $"Cards: {Game.PlayerHands[i].Count}", PlayerPos + new Vector2(10, 40), 18, 1, Color.Yellow);
                    }

                    // 中央出牌區
                    Rectangle tableArea = new(300, 180, 360, 180);
                    Raylib.DrawRectangleRec(tableArea, new(0, 60, 0, 150));
                    Raylib.DrawTextEx(font, "中央出牌區", new(420, 250), 24, 2, new Color(255, 255, 255, 50));
                    if(Game.LastPlay.Count > 0){
                        float startX = 330;
                        float Y = 210;

                        for(int i = 0; i < Game.LastPlay.Count; i++){
                            DrawCard(Game.LastPlay[i], startX + i * 50, Y);
                        }

                        Raylib.DrawTextEx(
                            font,
                            $"{Game.LastPlayerName} 出牌",
                            new(360, 190),
                            18,
                            1,
                            Color.Yellow
                        );
                    }

                    // 手牌區域
                    Raylib.DrawRectangle(150, 400, 660, 120, new Color(0, 0, 0, 50));
                    
                    // 手牌
                    for(int i = 0; i < MyHand.Count; i++){
                        float cardX = 180 + i*45; // 疊牌效果
                        float cardY = 420;
                        
                        bool hoverCard = Raylib.CheckCollisionPointRec(MousePos, new(cardX, cardY, 60, 90));
                        bool selected = SelectedCards.Any(c =>
                            c.Rank == MyHand[i].Rank &&
                            c.Suit == MyHand[i].Suit
                        );

                        if(selected)  cardY -= 20;

                        if(Game.CurrentTurn == MyIndex){
                            if(hoverCard && IsClick){
                                if(selected) SelectedCards.Remove(MyHand[i]); // 取消選
                                else         SelectedCards.Add(MyHand[i]);    // 選中  
                            }
                        }
                        else{
                            if(hoverCard) cardY -= 20; // 牌往上提
                        }
                        

                        // 畫牌
                        DrawCard(MyHand[i], cardX, cardY);
                    }

                    // 出牌 + Pass
                    Rectangle PlayButton = new(820, 420, 100, 40);
                    Rectangle PassButton = new(820, 470, 100, 40);

                    bool hoverPlay = Raylib.CheckCollisionPointRec(MousePos, PlayButton);
                    bool hoverPass = Raylib.CheckCollisionPointRec(MousePos, PassButton);

                    Color colorPlay;
                    Color colorPass;
                    if(Game.CurrentTurn == MyIndex){
                        // 顏色
                        colorPlay = hoverPlay ? Color.Lime : Color.Green;
                        colorPass = hoverPass ? Color.Orange : Color.Red;
                        // 換誰出牌
                        Raylib.DrawTextEx(font, "現在輪到你出牌...", new(200, 370), 22, 2, Color.Yellow);
                    }
                    else{
                        colorPlay = Color.Gray; colorPass = Color.Gray;
                    }

                    // 出牌
                    Raylib.DrawRectangleRec(PlayButton, colorPlay);
                    Raylib.DrawTextEx(font, "出牌", new(PlayButton.X + 28, PlayButton.Y + 10), 20, 2, Color.White);
                    if(hoverPlay && IsClick && Game.CurrentTurn == MyIndex){
                        var selected = GetSelectedCards();
                        var type = GetHandType(selected);
                        
                        // 合法?
                        if(!CanPlay(Game.LastPlay, selected)){
                            Console.WriteLine("牌型不合法");
                        }
                        else{
                            Console.WriteLine("出牌成功");
                            // 桌上牌
                            //Game.LastPlay = selected.ToList();
                            //Game.LastPlayerName = PlayerName;

                            // 送到 Server
                            client.SendPacket(
                                PacketType.PlayCard,
                                new GameData{
                                    PlayerHands = Game.PlayerHands,
                                    LastPlay = selected.ToList(),
                                    CurrentTurn = (Game.CurrentTurn + 1) % 4,
                                    LastPlayerName = PlayerName
                                }
                            );

                            // 從手牌移除
                            /*
                            foreach(var c in selected){
                                MyHand.Remove(c);
                            }*/

                            // 換人
                            Game.CurrentTurn = (Game.CurrentTurn + 1) % 4;
                            SelectedCards.Clear();

                            if(MyHand.Count == 0){
                                Console.WriteLine($"{PlayerName} 出完牌，遊戲結束！");
                                // 可呼叫 EndGame() 或其他結束邏輯
                            }

                        } 
                    }

                    Raylib.DrawRectangleRec(PassButton, colorPass);
                    Raylib.DrawTextEx(font, "PASS", new(PassButton.X + 25, PassButton.Y + 10), 20, 2, Color.White);
                    if(hoverPass && IsClick && Game.CurrentTurn == MyIndex){
                        Console.WriteLine("玩家選擇 PASS");

                        // 送封包給 server，通知本輪玩家 PASS
                        client.SendPacket(
                            PacketType.PlayCard,
                            new GameData{
                                PlayerHands = Game.PlayerHands,   // 玩家手牌不變
                                LastPlay = new List<Card>(),       // 沒有出牌
                                CurrentTurn = (Game.CurrentTurn + 1) % 4,
                                LastPlayerName = PlayerName
                            }
                        );

                        // 換下一個玩家 (client 先暫時更新 UI)
                        Game.CurrentTurn = (Game.CurrentTurn + 1) % 4;

                        SelectedCards.Clear();
                    }
                }
                Raylib.EndDrawing();

            }
            Raylib.UnloadTexture(LobbyBG);
            Raylib.UnloadImage(icon);
            Raylib.UnloadFont(font);
            Raylib.CloseWindow();
        }

        // ====================
        // 函式
        // ====================

        // 畫文字
        static void DrawLabel(string text, int x, int y){
            Raylib.DrawTextEx(font, text, new(x, y), 20, 2,Color.Black);
        }

        // 畫輸入框
        static void DrawBox(Rectangle rectangle, string text, bool isFocuse){
            // 框框顏色
            Raylib.DrawRectangleRec(rectangle, isFocuse ? Color.RayWhite : Color.DarkGray);
            Raylib.DrawRectangleLinesEx(rectangle, 3, isFocuse ? Color.Gold : Color.Gray);
            
            // 文字顏色
            Raylib.DrawTextEx(font, text, new((int)rectangle.X+10, (int)rectangle.Y+15), 24, 2, Color.Black);

            // 游標閃爍效果
            if(isFocuse){
                if((Raylib.GetTime() * 2) % 2 > 1){
                    Vector2 textSize = Raylib.MeasureTextEx(font, text, 24, 2);
                    Raylib.DrawRectangle((int)rectangle.X+10 + (int)textSize.X+2, (int)rectangle.Y+10, 2, 30, Color.Black);
                }
            }
        }

        // 輸入文字
        static void Input(ref string text){
            // 簡單輸入
            int Key = Raylib.GetCharPressed();
            while(Key > 0){
                if(Key >= 32 && text.Length < 9){
                    text += (char)Key;
                }
                Key = Raylib.GetCharPressed();
            }

            // 刪除輸入
            if(Raylib.IsKeyDown(KeyboardKey.Backspace)){
                float deltaTime = Raylib.GetFrameTime();
                Timer += deltaTime;

                // 刪除第一個字
                if(Raylib.IsKeyPressed(KeyboardKey.Backspace)){
                    if (text.Length > 0) text = text.Substring(0, text.Length - 1);
                    Timer = 0.0f;
                    TimerInterval = 0.0f;
                }

                // 常按刪除
                if(Timer > 0.5f){
                    TimerInterval += deltaTime;
                    
                    // 每秒刪20字
                    if(TimerInterval > 0.05f){
                        if(text.Length > 0) text = text.Substring(0, text.Length - 1);
                        TimerInterval = 0.0f;
                    }
                }
            }
            // 放開重置計時
            else{
                Timer = 0.0f;
                TimerInterval = 0.0f;
            }
        }

        // 畫牌
        static void DrawCard(Card card, float x, float y){
            Rectangle rec = new(x, y, 60, 90);

            Raylib.DrawRectangleRec(rec, Color.White);
            Raylib.DrawRectangleLinesEx(rec, 2, Color.Black);

            string rank = card.Rank
            switch{
                11 => "J",
                12 => "Q",
                13 => "K",
                14 => "A",
                15 => "2",
                _  => card.Rank.ToString()
            };

            string suit = card.Suit
            switch{
                Suit.Club    => "♣",
                Suit.Diamond => "♦",
                Suit.Heart   => "♥",
                Suit.Spade   => "♠",
                _ => ""
            };

            Color suitColor = (card.Suit == Suit.Heart || card.Suit == Suit.Diamond) ? Color.Red : Color.Black;
            Raylib.DrawTextEx(font2, rank, new(x + 6, y + 4), 20, 1, suitColor);
            Raylib.DrawTextEx(font2, suit, new(x + 6, y + 20), 24, 1, suitColor);
        }

        // 排序卡牌
        static int Big2RankValue(int rank){
            return rank switch{
                3  => 1,
                4  => 2,
                5  => 3,
                6  => 4,
                7  => 5,
                8  => 6,
                9  => 7,
                10 => 8,
                11 => 9,  // J
                12 => 10, // Q
                13 => 11, // K
                14 => 12, // A
                15 => 13, // 2
                _  => 0
            };
        }
        static int Big2SuitValue(Suit suit){
            // 花色大小：♣ < ♦ < ♥ < ♠
            return suit switch{
                Suit.Club    => 1,
                Suit.Diamond => 2,
                Suit.Heart   => 3,
                Suit.Spade   => 4,
                _ => 0
            };
        }
        
        // 選取的牌
        static List<Card> GetSelectedCards(){
            return SelectedCards.ToList();
        }
        static HandType GetHandType(List<Card> cards){
            if(cards.Count == 0) return HandType.Invalid;

            var ranks = cards.Select(c => c.Rank).OrderBy(r => r).ToList();
            var groups = ranks.GroupBy(r => r).Select(g => g.Count()).OrderBy(c => c).ToList();

            // 單張
            if(cards.Count == 1) 
                return HandType.Single;

            // 對子
            if(cards.Count == 2 && groups.SequenceEqual([2]))
                return HandType.Pair;

            // 三條
            if(cards.Count == 3 && groups.SequenceEqual([3]))
                return HandType.Triple;

            // 五張牌
            if(cards.Count == 5){
                bool isStraight = ranks.Distinct().Count() == 5 && ranks.Max() - ranks.Min() == 4;
                bool isFourKind = groups.SequenceEqual([1,4]);
                bool isFullHouse = groups.SequenceEqual([2,3]);

                if(isStraight) return HandType.Straight;
                if(isFullHouse) return HandType.FullHouse;
                if(isFourKind) return HandType.FourKind;
            }

            return HandType.Invalid;
        }
        static bool CanPlay(List<Card> lastPlay, List<Card> currentPlay){
            var currentType = GetHandType(currentPlay);
            if (currentType == HandType.Invalid)
                return false;

            // 桌上沒牌，直接可出
            if (lastPlay == null || lastPlay.Count == 0)
                return true;

            var lastType = GetHandType(lastPlay);

            // 牌型 & 張數必須相同
            if (currentType != lastType)
                return false;

            if (currentPlay.Count != lastPlay.Count)
                return false;

            // 比大小
            return CompareSameType(lastType, lastPlay, currentPlay);
        }
        static bool CompareSameType(
            HandType type,
            List<Card> last,
            List<Card> current
        ){
            switch(type){
                case HandType.Single:
                case HandType.Pair:
                case HandType.Triple:
                case HandType.Straight:
                    return CompareHighest(current, last);

                case HandType.FullHouse:
                    return GetTripleRank(current) > GetTripleRank(last);

                case HandType.FourKind:
                    return GetFourRank(current) > GetFourRank(last);

                default:
                    return false;
            }
        }
        static bool CompareHighest(List<Card> a, List<Card> b)
        {
            Card maxA = a
                .OrderBy(c => Big2RankValue(c.Rank))
                .ThenBy(c => Big2SuitValue(c.Suit))
                .Last();

            Card maxB = b
                .OrderBy(c => Big2RankValue(c.Rank))
                .ThenBy(c => Big2SuitValue(c.Suit))
                .Last();

            int r = Big2RankValue(maxA.Rank).CompareTo(Big2RankValue(maxB.Rank));
            if (r != 0) return r > 0;

            return Big2SuitValue(maxA.Suit) > Big2SuitValue(maxB.Suit);
        }
        static int GetTripleRank(List<Card> cards)
        {
            return cards
                .GroupBy(c => c.Rank)
                .First(g => g.Count() == 3)
                .Key;
        }
        static int GetFourRank(List<Card> cards)
        {
            return cards
                .GroupBy(c => c.Rank)
                .First(g => g.Count() == 4)
                .Key;
        }
    }
}