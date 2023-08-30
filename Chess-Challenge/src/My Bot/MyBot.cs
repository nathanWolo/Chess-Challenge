using ChessChallenge.API;
using System;
using static System.Math;
using System.Linq;
public class MyBot : IChessBot
{
    

    // zobrist hash, move, depth, score, bound
    private readonly (ulong, Move, int, int, int)[] transpositionTable = new (ulong, Move, int, int, int)[5_000_000]; //5M entries is approx 128MB, will fluctuate due to GC

    private readonly int[,,] historyTable = new int[2, 7, 64];
    private readonly Move[] killerTable = new Move[128];
    // public int positionsEvaluated = 0;
    public static int TIME_PER_MOVE = 0, CHECKMATE_SCORE = 9999999, aspiration = 15, pestoIndex=0;
    Move bestMoveRoot;
    /*
    PeSTO style tuned piece tables shamelessly stolen from TyrantBot
    */
    private static readonly short[] PieceValues = { 82, 337, 365, 477, 1025, 0, // Middlegame
                                           94, 281, 297, 512, 936, 0 }; // Endgame
        // Big table packed with data from premade piece square tables
        // Access using using PackedEvaluationTables[square][pieceType] = score
    private readonly int[][] 
        UnpackedPestoTables = new[] {
            63746705523041458768562654720m, 71818693703096985528394040064m, 75532537544690978830456252672m, 75536154932036771593352371712m, 76774085526445040292133284352m, 3110608541636285947269332480m, 936945638387574698250991104m, 75531285965747665584902616832m,
            77047302762000299964198997571m, 3730792265775293618620982364m, 3121489077029470166123295018m, 3747712412930601838683035969m, 3763381335243474116535455791m, 8067176012614548496052660822m, 4977175895537975520060507415m, 2475894077091727551177487608m,
            2458978764687427073924784380m, 3718684080556872886692423941m, 4959037324412353051075877138m, 3135972447545098299460234261m, 4371494653131335197311645996m, 9624249097030609585804826662m, 9301461106541282841985626641m, 2793818196182115168911564530m,
            77683174186957799541255830262m, 4660418590176711545920359433m, 4971145620211324499469864196m, 5608211711321183125202150414m, 5617883191736004891949734160m, 7150801075091790966455611144m, 5619082524459738931006868492m, 649197923531967450704711664m,
            75809334407291469990832437230m, 78322691297526401047122740223m, 4348529951871323093202439165m, 4990460191572192980035045640m, 5597312470813537077508379404m, 4980755617409140165251173636m, 1890741055734852330174483975m, 76772801025035254361275759599m,
            75502243563200070682362835182m, 78896921543467230670583692029m, 2489164206166677455700101373m, 4338830174078735659125311481m, 4960199192571758553533648130m, 3420013420025511569771334658m, 1557077491473974933188251927m, 77376040767919248347203368440m,
            73949978050619586491881614568m, 77043619187199676893167803647m, 1212557245150259869494540530m, 3081561358716686153294085872m, 3392217589357453836837847030m, 1219782446916489227407330320m, 78580145051212187267589731866m, 75798434925965430405537592305m,
            68369566912511282590874449920m, 72396532057599326246617936384m, 75186737388538008131054524416m, 77027917484951889231108827392m, 73655004947793353634062267392m, 76417372019396591550492896512m, 74568981255592060493492515584m, 70529879645288096380279255040m,
        }.Select(packedTable =>
        new System.Numerics.BigInteger(packedTable).ToByteArray().Take(12)
                    // Using search max time since it's an integer than initializes to zero and is assgined before being used again 
                    .Select(square => (int)((sbyte)square * 1.461) + PieceValues[pestoIndex++ % 12])
                .ToArray()
        ).ToArray();
    public Move Think(Board board, Timer timer)
    {
        
        TIME_PER_MOVE = timer.MillisecondsRemaining / 30; //use more time on lichess because of increment
        Array.Clear(historyTable, 0, historyTable.Length); //reset history table
        try {
            for (int depthLeft = 1, alpha = -CHECKMATE_SCORE, beta = CHECKMATE_SCORE, maxEval ;;) {
                //iterative deepening
                maxEval = NegaMax(board, depthLeft, 0, alpha, beta, timer);
                // Console.Write("best move: {0}, value: {1}, depth: {2}\n", bestMoveRoot, maxEval, depthLeft);
                //aspiration window
                if (maxEval <= alpha || maxEval >= beta) { //fail low or high
                    aspiration *= 2;
                    if (maxEval <= alpha) alpha -= aspiration;
                    else beta += aspiration;
                }
                else {
                    //reset aspiration window
                    aspiration = 15;
                    alpha = maxEval - aspiration;
                    beta = maxEval + aspiration;
                    depthLeft++;
                }
            }
        }
        catch {
            return bestMoveRoot;
        }
    }


    public int NegaMax(Board board, int depthLeft, int depthSoFar, int alpha, int beta, Timer timer)
    {
        if (timer.MillisecondsElapsedThisTurn > TIME_PER_MOVE || depthSoFar > 64) depthSoFar /= 0; //ran out of time
        bool inCheck = board.IsInCheck(), notRoot = depthSoFar != 0;
        if(inCheck) depthLeft++; //extend search depth if in check

        bool qsearch = depthLeft <= 0;
        Move bestMove = Move.NullMove;
        if (notRoot && (board.IsInsufficientMaterial() 
                        || board.IsRepeatedPosition() 
                        || board.FiftyMoveCounter >= 100)) {
            return 0;
        }
        ulong key = board.ZobristKey;
        ref var entry = ref transpositionTable[key % 5_000_000];
        int maxEval = -CHECKMATE_SCORE, entryScore = entry.Item4, entryBound = entry.Item5;
        if(notRoot && entry.Item1 == key //verify that the entry is for this position (can very rarely be wrong)
                && entry.Item3 >= depthLeft //verify that the entry is for a search of at least this depth
                && (entryBound == 3 // exact score
                    || (entryBound == 2 && entryScore >= beta )// lower bound, fail high
                    || (entryBound == 1 && entryScore <= alpha ))) {// upper bound, fail low
            // positionsEvaluated++;
            return entryScore;
        }


        //reverse futility pruning
        //Basic idea: if your score is so good you can take a big hit and still get the beta cutoff, go for it.
        int standPat = EvaluateBoard(board);
        if(qsearch) {
            maxEval = standPat;
            if(maxEval >= beta) return maxEval;
            alpha = Max(alpha, maxEval);
        }
        else if (depthLeft > 2 && !inCheck){ //null move pruning
            board.ForceSkipTurn();
            int nullEval = -NegaMax(board, depthLeft /2, depthSoFar + 1, -beta, -beta + 1, timer);
            board.UndoSkipTurn();
            if (nullEval >= beta) return beta; //doing nothing was able to raise beta, so we can prune

        }
        if (standPat - 150 * depthLeft >= beta  //TODO: tune this constant.
            && !qsearch ) {//dont prune in qsearch
            return beta; //fail hard, TODO: try fail soft
        }

        //deep futility pruning
        //basic idea: It discards the moves that have no potential of raising alpha, which in turn requires some estimate of a potential value of a move. 
        //This is calculated by adding a futility margin (representing the largest conceivable positional gain) to the evaluation of the current position.
        bool canPruneMove = depthLeft <= 8 && standPat + depthLeft * 225 <= alpha; //TODO: tune this constant


        Span<Move> legalMoves = stackalloc Move[256]; //stackalloc is faster than new
        board.GetLegalMovesNonAlloc(ref legalMoves, qsearch && !inCheck); //only generate captures in qsearch, but not if theres a check
        if (legalMoves.Length == 0 && !qsearch) {
                if (inCheck) return  -CHECKMATE_SCORE + depthSoFar;
                return 0;
            }

        Span<int> scores = stackalloc int[legalMoves.Length];
        //lower score -> search first
        for (int i = 0; i < legalMoves.Length; i++) {
            /*
            Move ordering hierarchy:
            1. TT move
            2. captures (MVV/LVA)
            3. Killers
            4. history heuristic
            */
            Move moveToBeScored = legalMoves[i];
            scores[i] = (moveToBeScored == entry.Item2 && entry.Item1 == key) ? -99999999 : //TT move
                moveToBeScored.IsCapture ? (int)moveToBeScored.MovePieceType - 1000000 * (int)moveToBeScored.CapturePieceType : //MVV/LVA
                killerTable[depthSoFar] == moveToBeScored ? -500000 : //killers
                historyTable[depthSoFar & 1, (int)moveToBeScored.MovePieceType, moveToBeScored.TargetSquare.Index]; //history heuristic
        }
        MemoryExtensions.Sort(scores, legalMoves);


        double origAlpha = alpha;
        for (int i =0; i < legalMoves.Length; i++) {
            
            if(canPruneMove && scores[i] == 0 && i > 0) continue; //prune move if it cant raise alpha, not a tactical move, and not the first move
            
            Move move = legalMoves[i];
            board.MakeMove(move);
            int eval = -NegaMax( board, depthLeft - 1, depthSoFar + 1, -beta, -alpha, timer); //promotion extension
            board.UndoMove(move);

            if (eval > maxEval)
            {
                maxEval = eval;
                bestMove = move;
                if (!notRoot && maxEval < beta && maxEval > origAlpha) 
                    bestMoveRoot = move; //is verifying the bounds here actually needed?
            }

            alpha = Max(alpha, maxEval);

            if (alpha >= beta){

                //update history and killer move tables
                if (!move.IsCapture) {  //dont update history for captures
                    historyTable[depthSoFar & 1, (int)move.MovePieceType, move.TargetSquare.Index] -= depthLeft * depthLeft;
                    killerTable[depthSoFar] = move;
                }
                break;
            }
        }
        //important to know if this is an exact score or just a lower/upper bound
        int bound = maxEval >= beta ? 2 : maxEval > origAlpha ? 3 : 1; // 3 = exact, 2 = lower bound, 1 = upper bound

        // Push to TT
        transpositionTable[key % 5_000_000] = (key, bestMove, depthLeft, maxEval, bound);
        
        return maxEval;
    }

        int EvaluateBoard(Board board)
        {
            int middlegame = 0, endgame = 0, gamephase = 0, sideToMove = 2, piece, square;
            for (; --sideToMove >= 0; middlegame = -middlegame, endgame = -endgame)
                for (piece = -1; ++piece < 6;)
                    for (ulong mask = board.GetPieceBitboard((PieceType)piece + 1, sideToMove > 0); mask != 0;)
                    {
                        // Gamephase, middlegame -> endgame
                        // Multiply, then shift, then mask out 4 bits for value (0-16)
                        gamephase += 0x00042110 >> piece * 4 & 0x0F;

                        // Material and square evaluation
                        square = BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^ 56 * sideToMove;
                        middlegame += UnpackedPestoTables[square][piece];
                        endgame += UnpackedPestoTables[square][piece + 6];
                    }
            // Tempo bonus to help with aspiration windows
            return (middlegame * gamephase + endgame * (24 - gamephase)) / 24 * (board.IsWhiteToMove ? 1 : -1) + gamephase / 2;
        }

}