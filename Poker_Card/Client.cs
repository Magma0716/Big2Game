using System;
using Raylib_cs;
using System.Linq;
using System.Text;
using System.Numerics;
using System.Text.Json;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;

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
    public class Card{
        public int Rank { get; set; }
        public Suit Suit { get; set; }
    }

    public class Program{
        // 字體
        static Font font;
        // 計時器
        static float Timer = 0.0f;
        static float TimerInterval = 0.0f;
        static void Main(string[] args){
            // 視窗
            Raylib.InitWindow(960, 540, "大老二");
            Raylib.SetTargetFPS(60);

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
            string En = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()_+-=[]{}|;':\",./<>? ";
            string Ch = "大老二玩家創建立遊戲房間名稱列表重新整理加入等待中央出牌區遊戲中主滿人即可離開始現在輪到你出牌";
            List<int> TextList = (En + Ch).Select(c => (int)c).Distinct().ToList();
            font = Raylib.LoadFontEx(@".\fonts\Cubic11.ttf", 48, TextList.ToArray(), TextList.Count);
            Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);

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
                    
                    // 找不到房間
                    if(Room == null){ 
                        CurrentScene = 1; 
                        continue; 
                    }

                    // 玩家索引
                    int PlayerIndex = Room.PlayerNames.IndexOf(PlayerName);

                    // 背景
                    Raylib.DrawRectangle(0, 0, 960, 540, new(0, 80, 0, 255));
                    Raylib.DrawRectangleLinesEx(new(10, 10, 940, 520), 5, Color.DarkGreen);

                    for(int i = 0; i < 4; i++){
                        // 跳過自己
                        if(i == PlayerIndex) continue; 

                        // 玩家位置
                        Vector2 PlayerPos = ((i - PlayerIndex + 4) % 4) 
                        switch{
                            1 => new Vector2(800, 200), // 右
                            2 => new Vector2(420, 50),  // 上
                            3 => new Vector2(50, 200),  // 左
                            _ => Vector2.Zero // 自己
                        };

                        // 對手資訊
                        Raylib.DrawRectangleV(PlayerPos, new(120, 80), new(0, 0, 0, 100));
                        Raylib.DrawTextEx(font, Room.PlayerNames[i], PlayerPos + new Vector2(10, 10), 20, 1, Color.White);
                        Raylib.DrawTextEx(font, "Cards: 13", PlayerPos + new Vector2(10, 40), 18, 1, Color.Yellow);
                    }

                    // 中央出牌區
                    Rectangle tableArea = new(300, 180, 360, 180);
                    Raylib.DrawRectangleRec(tableArea, new(0, 60, 0, 150));
                    Raylib.DrawTextEx(font, "中央出牌區", new(420, 250), 24, 2, new(255, 255, 255, 50));
                    
                    // 這裡應畫出最後一手的牌 (LastPlay)
                    // DrawCards(lastPlayCards, 400, 210); 

                    // 手牌區域
                    Raylib.DrawRectangle(150, 400, 660, 120, new(0, 0, 0, 50));
                    
                    // 模擬手牌 (建議另外寫一個 DrawCard 函式處理花色與點數)
                    for(int i = 0; i < 13; i++){
                        float cardX = 180 + i*45; // 疊牌效果
                        float cardY = 420;
                        
                        // 牌往上提
                        if(Raylib.CheckCollisionPointRec(MousePos, new(cardX, cardY, 60, 90))){
                            cardY -= 20;
                            if(IsClick){ 
                                /* 選取牌的邏輯 */
                            }
                        }

                        Raylib.DrawRectangle((int)cardX, (int)cardY, 60, 90, Color.RayWhite);
                        Raylib.DrawRectangleLines((int)cardX, (int)cardY, 60, 90, Color.Black);
                        Raylib.DrawText("?", (int)cardX + 20, (int)cardY + 30, 30, Color.Black);
                    }

                    // 出牌 + Pass
                    Rectangle PlayButton = new(820, 420, 100, 40);
                    Rectangle PassButton = new(820, 470, 100, 40);

                    bool hoverPlay = Raylib.CheckCollisionPointRec(MousePos, PlayButton);
                    bool hoverPass = Raylib.CheckCollisionPointRec(MousePos, PassButton);

                    Raylib.DrawRectangleRec(PlayButton, hoverPlay ? Color.Lime : Color.Green);
                    Raylib.DrawTextEx(font, "出牌", new(PlayButton.X + 28, PlayButton.Y + 10), 20, 2, Color.White);

                    Raylib.DrawRectangleRec(PassButton, hoverPass ? Color.Orange : Color.Red);
                    Raylib.DrawTextEx(font, "PASS", new(PassButton.X + 25, PassButton.Y + 10), 20, 2, Color.White);

                    // 換誰出牌
                    Raylib.DrawTextEx(font, "現在輪到你出牌...", new(200, 370), 22, 2, Color.Yellow);
                }
                Raylib.EndDrawing();
            }

            Raylib.UnloadTexture(LobbyBG);
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

        // 比較牌
        public static int Compare(Card a, Card b){
            if(a.Rank != b.Rank)
                return a.Rank.CompareTo(b.Rank);
            return a.Suit.CompareTo(b.Suit);
        }

    }
}