using ChessChallenge.Chess;
using ChessChallenge.Example;
using Raylib_cs;
using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static ChessChallenge.Application.Settings;
using static ChessChallenge.Application.ConsoleHelper;

namespace ChessChallenge.Application
{
    public class ChallengeController
    {
        int totalMovesPlayed = 0;
        public int trueTotalMovesPlayed = 0;
        public enum PlayerType
        {
            Human,
            MyBot,
            EvilBot,
            MyNaiveMinimax,
            MyABMinimax,

            MyABNegamax,

            MyABNegamaxV2,

            DiscordPracticeBot,

            MyIterDeepV1, 
            MyIterDeepPSEV1,
            MyIterDeepPSEV2,
            MyTTV1,
            MyAspirationV1,
            MyAspirationV2,
            MyAspirationV3,
            MyAspirationV4,
            MyPackedEvalV1,
            MyPackedEvalV2,
            MyPackedEvalV3,
            MyQsearchV1,
            MyQsearchV2,
            MyQsearchV3,
            MyQsearchV4,
            MyQsearchV5,
            MyQsearchV6,
            MyQsearchV7,
            MyQsearchV8,
            MyQsearchV9,
            MyQsearchV10,
            MyQsearchV11,
            MyQsearchV12,
            Benchmark1,
            Benchmark2,
        }

        // Game state
        readonly Random rng;
        int gameID;
        bool isPlaying;
        Board board;
        public ChessPlayer PlayerWhite { get; private set; }
        public ChessPlayer PlayerBlack {get;private set;}

        float lastMoveMadeTime;
        bool isWaitingToPlayMove;
        Move moveToPlay;
        float playMoveTime;
        public bool HumanWasWhiteLastGame { get; private set; }

        // Bot match state
        readonly string[] botMatchStartFens;
        int botMatchGameIndex;
        public BotMatchStats BotStatsA { get; private set; }
        public BotMatchStats BotStatsB {get;private set;}
        bool botAPlaysWhite;


        // Bot task
        AutoResetEvent botTaskWaitHandle;
        bool hasBotTaskException;
        ExceptionDispatchInfo botExInfo;

        // Other
        readonly BoardUI boardUI;
        readonly MoveGenerator moveGenerator;
        readonly int tokenCount;
        readonly int debugTokenCount;
        readonly StringBuilder pgns;

        public ChallengeController()
        {
            Log($"Launching Chess-Challenge version {Settings.Version}");
            (tokenCount, debugTokenCount) = GetTokenCount();
            Warmer.Warm();

            rng = new Random();
            moveGenerator = new();
            boardUI = new BoardUI();
            board = new Board();
            pgns = new();

            BotStatsA = new BotMatchStats("IBot");
            BotStatsB = new BotMatchStats("IBot");
            botMatchStartFens = FileHelper.ReadResourceFile("Fens.txt").Split('\n').Where(fen => fen.Length > 0).ToArray();
            botTaskWaitHandle = new AutoResetEvent(false);

            StartNewGame(PlayerType.Human, PlayerType.MyBot);
        }

        public void StartNewGame(PlayerType whiteType, PlayerType blackType)
        {
            // End any ongoing game
            EndGame(GameResult.DrawByArbiter, log: false, autoStartNextBotMatch: false);
            gameID = rng.Next();
            totalMovesPlayed = 0;
            trueTotalMovesPlayed += totalMovesPlayed;
            // Stop prev task and create a new one
            if (RunBotsOnSeparateThread)
            {
                // Allow task to terminate
                botTaskWaitHandle.Set();
                // Create new task
                botTaskWaitHandle = new AutoResetEvent(false);
                Task.Factory.StartNew(BotThinkerThread, TaskCreationOptions.LongRunning);
            }
            // Board Setup
            board = new Board();
            bool isGameWithHuman = whiteType is PlayerType.Human || blackType is PlayerType.Human;
            int fenIndex = isGameWithHuman ? 0 : botMatchGameIndex / 2;
            board.LoadPosition(botMatchStartFens[fenIndex]);

            // Player Setup
            PlayerWhite = CreatePlayer(whiteType);
            PlayerBlack = CreatePlayer(blackType);
            PlayerWhite.SubscribeToMoveChosenEventIfHuman(OnMoveChosen);
            PlayerBlack.SubscribeToMoveChosenEventIfHuman(OnMoveChosen);

            // UI Setup
            boardUI.UpdatePosition(board);
            boardUI.ResetSquareColours();
            SetBoardPerspective();

            // Start
            isPlaying = true;
            NotifyTurnToMove();
        }
public static ChessChallenge.API.IChessBot? CreateBot(PlayerType type)
{
    return type switch
    {
        PlayerType.MyBot => new MyBot(),
        PlayerType.EvilBot => new EvilBot(),
        PlayerType.MyQsearchV1 => new MyQsearchV1(),
        PlayerType.MyQsearchV2 => new MyQsearchV2(),
        PlayerType.MyQsearchV3 => new MyQsearchV3(),
        PlayerType.MyQsearchV4 => new MyQsearchV4(),
        PlayerType.MyQsearchV5 => new MyQsearchV5(),
        PlayerType.MyQsearchV6 => new MyQsearchV6(),
        PlayerType.MyQsearchV7 => new MyQsearchV7(),
        PlayerType.MyQsearchV8 => new MyQsearchV8(),
        PlayerType.MyQsearchV9 => new MyQsearchV9(),
        PlayerType.MyQsearchV10 => new MyQsearchV10(),
        PlayerType.MyQsearchV11 => new MyQsearchV11(),
        PlayerType.MyQsearchV12 => new MyQsearchV12(),
        PlayerType.MyNaiveMinimax => new MyNaiveMinimax(),
        PlayerType.MyIterDeepV1 => new MyIterDeepV1(),
        PlayerType.MyIterDeepPSEV1 => new MyIterDeepPSEV1(),
        PlayerType.MyIterDeepPSEV2 => new MyIterDeepPSEV2(),
        PlayerType.MyTTV1 => new MyTTV1(),
        PlayerType.MyAspirationV1 => new MyAspirationV1(),
        PlayerType.MyAspirationV2 => new MyAspirationV2(),
        PlayerType.MyAspirationV3 => new MyAspirationV3(),
        PlayerType.MyAspirationV4 => new MyAspirationV4(),
        PlayerType.MyPackedEvalV1 => new MyPackedEvalV1(),
        PlayerType.MyPackedEvalV2 => new MyPackedEvalV2(),
        PlayerType.MyPackedEvalV3 => new MyPackedEvalV3(),
        PlayerType.MyABMinimax => new MyABMinimax(),
        PlayerType.MyABNegamax => new MyABNegamax(),
        PlayerType.MyABNegamaxV2 => new MyABNegamaxV2(),
        PlayerType.Benchmark1 => new Benchmark1(),
        PlayerType.Benchmark2 => new Benchmark2(),
        // If you have other bot types, you can add them here as well
        _ => null
    };
}
        void BotThinkerThread()
        {
            int threadID = gameID;
            //Console.WriteLine("Starting thread: " + threadID);

            while (true)
            {
                // Sleep thread until notified
                botTaskWaitHandle.WaitOne();
                // Get bot move
                if (threadID == gameID)
                {
                    var move = GetBotMove();

                    if (threadID == gameID)
                    {
                        OnMoveChosen(move);
                    }
                }
                // Terminate if no longer playing this game
                if (threadID != gameID)
                {
                    break;
                }
            }
            //Console.WriteLine("Exitting thread: " + threadID);
        }

        Move GetBotMove()
        {
            API.Board botBoard = new(board);
            try
            {
                API.Timer timer = new(PlayerToMove.TimeRemainingMs, PlayerNotOnMove.TimeRemainingMs, GameDurationMilliseconds, IncrementMilliseconds);
                API.Move move = PlayerToMove.Bot.Think(botBoard, timer);
                totalMovesPlayed++;
                return new Move(move.RawValue);
            }
            catch (Exception e)
            {
                Log("An error occurred while bot was thinking.\n" + e.ToString(), true, ConsoleColor.Red);
                hasBotTaskException = true;
                botExInfo = ExceptionDispatchInfo.Capture(e);
            }
            return Move.NullMove;
        }



        void NotifyTurnToMove()
        {
            //playerToMove.NotifyTurnToMove(board);
            if (PlayerToMove.IsHuman)
            {
                PlayerToMove.Human.SetPosition(FenUtility.CurrentFen(board));
                PlayerToMove.Human.NotifyTurnToMove();
            }
            else
            {
                if (RunBotsOnSeparateThread)
                {
                    botTaskWaitHandle.Set();
                }
                else
                {
                    double startThinkTime = Raylib.GetTime();
                    var move = GetBotMove();
                    double thinkDuration = Raylib.GetTime() - startThinkTime;
                    PlayerToMove.UpdateClock(thinkDuration);
                    OnMoveChosen(move);
                }
            }
        }

        void SetBoardPerspective()
        {
            // Board perspective
            if (PlayerWhite.IsHuman || PlayerBlack.IsHuman)
            {
                boardUI.SetPerspective(PlayerWhite.IsHuman);
                HumanWasWhiteLastGame = PlayerWhite.IsHuman;
            }
            else if (PlayerWhite.Bot is MyBot && PlayerBlack.Bot is MyBot)
            {
                boardUI.SetPerspective(true);
            }
            else
            {
                boardUI.SetPerspective(PlayerWhite.Bot is MyBot);
            }
        }

        ChessPlayer CreatePlayer(PlayerType type)
        {
            return type switch
            {
                PlayerType.MyBot => new ChessPlayer(new MyBot(), type, GameDurationMilliseconds),
                PlayerType.EvilBot => new ChessPlayer(new EvilBot(), type, GameDurationMilliseconds),
                PlayerType.MyNaiveMinimax => new ChessPlayer(new MyNaiveMinimax(), type, GameDurationMilliseconds),
                PlayerType.MyABMinimax => new ChessPlayer(new MyABMinimax(), type, GameDurationMilliseconds),
                PlayerType.MyABNegamax => new ChessPlayer(new MyABNegamax(), type, GameDurationMilliseconds),
                PlayerType.MyABNegamaxV2 => new ChessPlayer(new MyABNegamaxV2(), type, GameDurationMilliseconds),
                PlayerType.MyIterDeepV1 => new ChessPlayer(new MyIterDeepV1(), type, GameDurationMilliseconds),
                PlayerType.MyIterDeepPSEV1 => new ChessPlayer(new MyIterDeepPSEV1(), type, GameDurationMilliseconds),
                PlayerType.MyIterDeepPSEV2 => new ChessPlayer(new MyIterDeepPSEV2(), type, GameDurationMilliseconds),
                PlayerType.MyTTV1 => new ChessPlayer(new MyTTV1(), type, GameDurationMilliseconds),
                PlayerType.MyAspirationV1 => new ChessPlayer(new MyAspirationV1(), type, GameDurationMilliseconds),
                PlayerType.MyAspirationV2 => new ChessPlayer(new MyAspirationV2(), type, GameDurationMilliseconds),
                PlayerType.MyAspirationV3 => new ChessPlayer(new MyAspirationV3(), type, GameDurationMilliseconds),
                PlayerType.MyAspirationV4 => new ChessPlayer(new MyAspirationV4(), type, GameDurationMilliseconds),
                PlayerType.MyPackedEvalV1 => new ChessPlayer(new MyPackedEvalV1(), type, GameDurationMilliseconds),
                PlayerType.MyPackedEvalV2 => new ChessPlayer(new MyPackedEvalV2(), type, GameDurationMilliseconds),
                PlayerType.MyPackedEvalV3 => new ChessPlayer(new MyPackedEvalV3(), type, GameDurationMilliseconds),
                PlayerType.MyQsearchV1 => new ChessPlayer(new MyQsearchV1(), type, GameDurationMilliseconds),
                PlayerType.MyQsearchV2 => new ChessPlayer(new MyQsearchV2(), type, GameDurationMilliseconds),
                PlayerType.MyQsearchV3 => new ChessPlayer(new MyQsearchV3(), type, GameDurationMilliseconds),
                PlayerType.MyQsearchV4 => new ChessPlayer(new MyQsearchV4(), type, GameDurationMilliseconds),
                PlayerType.MyQsearchV5 => new ChessPlayer(new MyQsearchV5(), type, GameDurationMilliseconds),
                PlayerType.MyQsearchV6 => new ChessPlayer(new MyQsearchV6(), type, GameDurationMilliseconds),
                PlayerType.MyQsearchV7 => new ChessPlayer(new MyQsearchV7(), type, GameDurationMilliseconds),
                PlayerType.MyQsearchV8 => new ChessPlayer(new MyQsearchV8(), type, GameDurationMilliseconds),
                PlayerType.MyQsearchV9 => new ChessPlayer(new MyQsearchV9(), type, GameDurationMilliseconds),
                PlayerType.MyQsearchV10 => new ChessPlayer(new MyQsearchV10(), type, GameDurationMilliseconds),
                PlayerType.MyQsearchV11 => new ChessPlayer(new MyQsearchV11(), type, GameDurationMilliseconds),
                PlayerType.MyQsearchV12 => new ChessPlayer(new MyQsearchV12(), type, GameDurationMilliseconds),
                PlayerType.Benchmark1 => new ChessPlayer(new Benchmark1(), type, GameDurationMilliseconds),
                PlayerType.Benchmark2 => new ChessPlayer(new Benchmark2(), type, GameDurationMilliseconds),
                _ => new ChessPlayer(new HumanPlayer(boardUI), type)
            };
        }

        static (int totalTokenCount, int debugTokenCount) GetTokenCount()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "src", "My Bot", "MyBot.cs");

            using StreamReader reader = new(path);
            string txt = reader.ReadToEnd();
            return TokenCounter.CountTokens(txt);
        }

        void OnMoveChosen(Move chosenMove)
        {
            if (IsLegal(chosenMove))
            {
                PlayerToMove.AddIncrement(IncrementMilliseconds);
                if (PlayerToMove.IsBot)
                {
                    moveToPlay = chosenMove;
                    isWaitingToPlayMove = true;
                    playMoveTime = lastMoveMadeTime + MinMoveDelay;
                }
                else
                {
                    PlayMove(chosenMove);
                }
            }
            else
            {
                string moveName = MoveUtility.GetMoveNameUCI(chosenMove);
                string log = $"Illegal move: {moveName} in position: {FenUtility.CurrentFen(board)}";
                Log(log, true, ConsoleColor.Red);
                GameResult result = PlayerToMove == PlayerWhite ? GameResult.WhiteIllegalMove : GameResult.BlackIllegalMove;
                EndGame(result);
            }
        }

        void PlayMove(Move move)
        {
            if (isPlaying)
            {
                bool animate = PlayerToMove.IsBot;
                lastMoveMadeTime = (float)Raylib.GetTime();

                board.MakeMove(move, false);
                boardUI.UpdatePosition(board, move, animate);

                GameResult result = Arbiter.GetGameState(board);
                if (result == GameResult.InProgress)
                {
                    NotifyTurnToMove();
                }
                else
                {
                    EndGame(result);
                }
            }
        }

        void EndGame(GameResult result, bool log = true, bool autoStartNextBotMatch = true)
        {
            if (isPlaying)
            {
                isPlaying = false;
                isWaitingToPlayMove = false;
                gameID = -1;

                if (log)
                {
                    Log("Game Over: " + result, false, ConsoleColor.Blue);
                }

                string pgn = PGNCreator.CreatePGN(board, result, GetPlayerName(PlayerWhite), GetPlayerName(PlayerBlack));
                pgns.AppendLine(pgn);

                // If 2 bots playing each other, start next game automatically.
                if (PlayerWhite.IsBot && PlayerBlack.IsBot)
                {
                    UpdateBotMatchStats(result);
                    botMatchGameIndex++;
                    int numGamesToPlay = botMatchStartFens.Length * 2;

                    if (botMatchGameIndex < numGamesToPlay && autoStartNextBotMatch)
                    {
                        botAPlaysWhite = !botAPlaysWhite;
                        const int startNextGameDelayMs = 600;
                        System.Timers.Timer autoNextTimer = new(startNextGameDelayMs);
                        int originalGameID = gameID;
                        autoNextTimer.Elapsed += (s, e) => AutoStartNextBotMatchGame(originalGameID, autoNextTimer);
                        autoNextTimer.AutoReset = false;
                        autoNextTimer.Start();

                    }
                    else if (autoStartNextBotMatch)
                    {
                        Log("Match finished", false, ConsoleColor.Blue);
                    }
                }
            }
        }

        private void AutoStartNextBotMatchGame(int originalGameID, System.Timers.Timer timer)
        {
            if (originalGameID == gameID)
            {
                StartNewGame(PlayerBlack.PlayerType, PlayerWhite.PlayerType);
            }
            timer.Close();
        }


        void UpdateBotMatchStats(GameResult result)
        {
            UpdateStats(BotStatsA, botAPlaysWhite);
            UpdateStats(BotStatsB, !botAPlaysWhite);

            void UpdateStats(BotMatchStats stats, bool isWhiteStats)
            {
                // Draw
                if (Arbiter.IsDrawResult(result))
                {
                    stats.NumDraws++;
                }
                // Win
                else if (Arbiter.IsWhiteWinsResult(result) == isWhiteStats)
                {
                    stats.NumWins++;
                }
                // Loss
                else
                {
                    stats.NumLosses++;
                    stats.NumTimeouts += (result is GameResult.WhiteTimeout or GameResult.BlackTimeout) ? 1 : 0;
                    stats.NumIllegalMoves += (result is GameResult.WhiteIllegalMove or GameResult.BlackIllegalMove) ? 1 : 0;
                }
            }
        }

        public void Update()
        {
            if (isPlaying)
            {
                PlayerWhite.Update();
                PlayerBlack.Update();

                PlayerToMove.UpdateClock(Raylib.GetFrameTime());
                if (PlayerToMove.TimeRemainingMs <= 0)
                {
                    EndGame(PlayerToMove == PlayerWhite ? GameResult.WhiteTimeout : GameResult.BlackTimeout);
                }
                else
                {
                    if (isWaitingToPlayMove && Raylib.GetTime() > playMoveTime)
                    {
                        isWaitingToPlayMove = false;
                        PlayMove(moveToPlay);
                    }
                }
            }

            if (hasBotTaskException)
            {
                hasBotTaskException = false;
                botExInfo.Throw();
            }
        }

        public void Draw()
        {
            boardUI.Draw();
            string nameW = GetPlayerName(PlayerWhite);
            string nameB = GetPlayerName(PlayerBlack);
            boardUI.DrawPlayerNames(nameW, nameB, PlayerWhite.TimeRemainingMs, PlayerBlack.TimeRemainingMs, isPlaying);
        }

        public void DrawOverlay()
        {
            BotBrainCapacityUI.Draw(tokenCount, debugTokenCount, MaxTokenCount);
            MenuUI.DrawButtons(this);
            MatchStatsUI.DrawMatchStats(this);
        }

        static string GetPlayerName(ChessPlayer player) => GetPlayerName(player.PlayerType);
        static string GetPlayerName(PlayerType type) => type.ToString();

        public void StartNewBotMatch(PlayerType botTypeA, PlayerType botTypeB)
        {
            EndGame(GameResult.DrawByArbiter, log: false, autoStartNextBotMatch: false);
            botMatchGameIndex = 0;
            string nameA = GetPlayerName(botTypeA);
            string nameB = GetPlayerName(botTypeB);
            if (nameA == nameB)
            {
                nameA += " (A)";
                nameB += " (B)";
            }
            BotStatsA = new BotMatchStats(nameA);
            BotStatsB = new BotMatchStats(nameB);
            botAPlaysWhite = true;
            Log($"Starting new match: {nameA} vs {nameB}", false, ConsoleColor.Blue);
            StartNewGame(botTypeA, botTypeB);
        }


        ChessPlayer PlayerToMove => board.IsWhiteToMove ? PlayerWhite : PlayerBlack;
        ChessPlayer PlayerNotOnMove => board.IsWhiteToMove ? PlayerBlack : PlayerWhite;

        public int TotalGameCount => botMatchStartFens.Length * 2;
        public int CurrGameNumber => Math.Min(TotalGameCount, botMatchGameIndex + 1);
        public string AllPGNs => pgns.ToString();


        bool IsLegal(Move givenMove)
        {
            var moves = moveGenerator.GenerateMoves(board);
            foreach (var legalMove in moves)
            {
                if (givenMove.Value == legalMove.Value)
                {
                    return true;
                }
            }

            return false;
        }

        public class BotMatchStats
        {
            public string BotName;
            public int NumWins;
            public int NumLosses;
            public int NumDraws;
            public int NumTimeouts;
            public int NumIllegalMoves;

            public BotMatchStats(string name) => BotName = name;
        }

        public void Release()
        {
            boardUI.Release();
        }
    }
}
