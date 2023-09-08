using ChessChallenge.API;
using System;
using static System.Math;
using System.Linq;
public class MyBot : IChessBot
{
    

    // zobrist hash, move, depth, score, bound

    //TODO: use smaller types for depth, score and bound.
    // if we go int16, int8, int8 thats a whole byte smaller for each entry
    //will have to reorder so biggest types are first
    private readonly (ulong, Move, int, int, int)[] transpositionTable = new (ulong, Move, int, int, int)[5_000_000]; //5M entries is approx 128MB, will fluctuate due to GC

    private readonly int[,,] historyTable = new int[2, 7, 64];
    private readonly Move[] killerTable = new Move[256];
    // public int positionsEvaluated = 0;
    public static int TIME_PER_MOVE = 0, aspiration = 12;
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
            63746705523041458768562654720m, 71818693703096985528394040064m, 75532537544690978830456252672m, 75536154932036771593352371712m, 
            76774085526445040292133284352m, 3110608541636285947269332480m, 936945638387574698250991104m, 75531285965747665584902616832m,
            77047302762000299964198997571m, 3730792265775293618620982364m, 3121489077029470166123295018m, 3747712412930601838683035969m, 
            3763381335243474116535455791m, 8067176012614548496052660822m, 4977175895537975520060507415m, 2475894077091727551177487608m,
            2458978764687427073924784380m, 3718684080556872886692423941m, 4959037324412353051075877138m, 3135972447545098299460234261m, 
            4371494653131335197311645996m, 9624249097030609585804826662m, 9301461106541282841985626641m, 2793818196182115168911564530m,
            77683174186957799541255830262m, 4660418590176711545920359433m, 4971145620211324499469864196m, 5608211711321183125202150414m, 
            5617883191736004891949734160m, 7150801075091790966455611144m, 5619082524459738931006868492m, 649197923531967450704711664m,
            75809334407291469990832437230m, 78322691297526401047122740223m, 4348529951871323093202439165m, 4990460191572192980035045640m, 
            5597312470813537077508379404m, 4980755617409140165251173636m, 1890741055734852330174483975m, 76772801025035254361275759599m,
            75502243563200070682362835182m, 78896921543467230670583692029m, 2489164206166677455700101373m, 4338830174078735659125311481m, 
            4960199192571758553533648130m, 3420013420025511569771334658m, 1557077491473974933188251927m, 77376040767919248347203368440m,
            73949978050619586491881614568m, 77043619187199676893167803647m, 1212557245150259869494540530m, 3081561358716686153294085872m, 
            3392217589357453836837847030m, 1219782446916489227407330320m, 78580145051212187267589731866m, 75798434925965430405537592305m,
            68369566912511282590874449920m, 72396532057599326246617936384m, 75186737388538008131054524416m, 77027917484951889231108827392m, 
            73655004947793353634062267392m, 76417372019396591550492896512m, 74568981255592060493492515584m, 70529879645288096380279255040m,
        }.Select(packedTable =>
        new System.Numerics.BigInteger(packedTable).ToByteArray().Take(12)
                    .Select(square => (int)((sbyte)square * 1.461) + PieceValues[aspiration++ % 12])
                .ToArray()
        ).ToArray();

        Board globalBoard;
        Timer globalTimer;
    public Move Think(Board board, Timer timer)
    {
        globalBoard = board;
        globalTimer = timer;
        TIME_PER_MOVE = timer.MillisecondsRemaining / 30; //use more time on lichess because of increment
        Array.Clear(historyTable, 0, 896); //reset history table, TODO: replace historyTable.Length with a constant of 896
        try {
            for (int depthLeft = 1, alpha = -12_000, beta = 12_000, maxEval ;;) {
                //iterative deepening
                maxEval = PVS(depthLeft, 0, alpha, beta);
                
                // Console.Write("best move: {0}, value: {1}, depth: {2}\n", bestMoveRoot, maxEval, depthLeft);
                //aspiration window
                if (maxEval <= alpha || maxEval >= beta) { //fail low or high
                    aspiration *= 2;
                    if (maxEval <= alpha) alpha -= aspiration;
                    else beta += aspiration;
                }
                else {
                    //reset aspiration window
                    aspiration = 12;
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


    public int PVS(int depthLeft, int depthSoFar, int alpha, int beta)
    {
        if (globalTimer.MillisecondsElapsedThisTurn > TIME_PER_MOVE) depthSoFar /= 0;
        bool inCheck = globalBoard.IsInCheck(), notRoot = depthSoFar != 0, notPV = beta == alpha + 1, canFutilityPrune=false;
        if(inCheck) depthLeft++; //extend search depth if in check

        bool qsearch = depthLeft <= 0;
        Move bestMove = default;

        if (notRoot && globalBoard.IsRepeatedPosition()) return 0;

        ulong boardKey = globalBoard.ZobristKey;
        int maxEval = -12_000, eval, standPat = EvaluateBoard();
        
        var (entryKey, entryMove, entryDepth, entryScore, entryBound) = transpositionTable[boardKey % 5_000_000];

        //weird ass local method bs to save on tokens
        int Search(int newAlpha, int reduction = 1) => eval = -PVS(depthLeft - reduction, depthSoFar + 1, -newAlpha, -alpha);

        //transposition  table lookup
        if(notRoot && entryKey == boardKey //verify that the entry is for this position (can very rarely be wrong)
                && entryDepth >= depthLeft //verify that the entry is for a search of at least this depth
                && (entryBound == 3 // exact score
                    || (entryBound == 2 && entryScore >= beta )// lower bound, fail high
                    || (entryBound == 1 && entryScore <= alpha ))) {// upper bound, fail low
            return entryScore;
        }



        if(qsearch) {
            maxEval = standPat;
            if(maxEval >= beta) return beta;
            alpha = Max(alpha, maxEval);
        }
        else if (notPV && !inCheck) {
            if (depthLeft > 2){ //null move pruning
                globalBoard.ForceSkipTurn();
                // eval = -PVS(depthLeft/2, depthSoFar + 1, -beta, -beta + 1);
                Search(beta, 3 + depthLeft / 4);
                globalBoard.UndoSkipTurn();
                if (eval >= beta) return beta; //doing nothing was able to raise beta, so we can prune

            }
            //reverse futility pruning
            //Basic idea: if your score is so good you can take a big hit and still get the beta cutoff, go for it.
            if (standPat - 90 * depthLeft >= beta && depthLeft < 8) //TODO: tune this constant.
                //margin was originally 150
                //testing 125:
                /*
                Score of dev rfp margin 90 vs Onion72: 994 - 847 - 1094  [0.525] 2935
                ...      dev rfp margin 90 playing White: 627 - 306 - 535  [0.609] 1468
                ...      dev rfp margin 90 playing Black: 367 - 541 - 559  [0.441] 1467
                ...      White vs Black: 1168 - 673 - 1094  [0.584] 2935
                Elo difference: 17.4 +/- 9.9, LOS: 100.0 %, DrawRatio: 37.3 %
                SPRT: llr 2.89 (100.1%), lbound -2.25, ubound 2.89 - H1 was accepted
                */
                return beta; //fail hard, TODO: try fail soft
            
            canFutilityPrune = depthLeft <= 8 && standPat + depthLeft * 160 <= alpha;
            //margin was originally 225
            //testing 160: 
            /*
            Score of dev fp margin 125 vs Onion73: 1021 - 927 - 1149  [0.515] 3097
            ...      dev fp margin 125 playing White: 667 - 306 - 576  [0.617] 1549
            ...      dev fp margin 125 playing Black: 354 - 621 - 573  [0.414] 1548
            ...      White vs Black: 1288 - 660 - 1149  [0.601] 3097
            Elo difference: 10.5 +/- 9.7, LOS: 98.3 %, DrawRatio: 37.1 %
            SPRT: llr 1.64 (56.8%), lbound -2.25, ubound 2.89
            */
        }


        Span<Move> legalMoves = stackalloc Move[256]; //stackalloc is faster than new
        globalBoard.GetLegalMovesNonAlloc(ref legalMoves, qsearch && !inCheck); //only generate captures in qsearch, but not if theres a check
        int origAlpha = alpha, numMoves = legalMoves.Length, moveIndex = -1;
        if (numMoves == 0 && !qsearch) {
                return inCheck ? -12_000 + depthSoFar : 0;
            }

        Span<int> scores = stackalloc int[numMoves];
        //lower score -> search first
        while(++moveIndex < numMoves) {
            /*
            Move ordering hierarchy:
            1. TT move
            2. captures (MVV/LVA)
            3. Killers
            4. history heuristic
            */
            bestMove = legalMoves[moveIndex];
            scores[moveIndex] = (bestMove == entryMove && entryKey == boardKey) ? -999_999_999 : //TT move
                bestMove.IsCapture ? (int)bestMove.MovePieceType - 10_000_000 * (int)bestMove.CapturePieceType : //MVV/LVA
                killerTable[depthSoFar] == bestMove ? -5_000_000 : //killers
                historyTable[depthSoFar & 1, (int)bestMove.MovePieceType, bestMove.TargetSquare.Index]; //history heuristic
        }
        MemoryExtensions.Sort(scores, legalMoves);

        moveIndex = -1;
        while (++moveIndex < numMoves) {
            
            Move move = legalMoves[moveIndex];
            //use single ands to avoid compiler shortcutting on &&s
            //this way our increment is always executed
            //late move reduction condition
            bool quiet = scores[moveIndex] == 0; //move is not TT, capture, killer, or history
            if(canFutilityPrune && quiet) //can fp is only set to true in a not PV, not Qsearch if block, so safe to have it here.
                    //scores[moveIndex] == 0 asserts that the move is quiet and hasnt caused beta cutoffs in the past
                    continue;
            //futility pruning
            globalBoard.MakeMove(move);
            //PVS
            if (moveIndex == 0 || qsearch) 
                Search(beta);
            
            else {
                Search(alpha +1, quiet ? 1 + (int)(Log(depthLeft) * Log(moveIndex) / 2) : 1); //lmr formula stolen from ethereal
                if ((quiet || eval < beta) &&  eval > alpha) Search(beta); //re-search if failed high
            }
            globalBoard.UndoMove(move);

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
        transpositionTable[boardKey % 5_000_000] = (boardKey, bestMove, depthLeft, maxEval, bound);
        
        return maxEval;
    }

        int EvaluateBoard() //Shamelessly stolen from TyrantBot
        {
            int middlegame = 0, endgame = 0, gamephase = 0, sideToMove = 2, piece, square;
            for (; --sideToMove >= 0; middlegame = -middlegame, endgame = -endgame)
                for (piece = -1; ++piece < 6;)
                    for (ulong mask = globalBoard.GetPieceBitboard((PieceType)piece + 1, sideToMove > 0); mask != 0;)
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
            return (middlegame * gamephase + endgame * (24 - gamephase)) / 24 * (globalBoard.IsWhiteToMove ? 1 : -1) + gamephase / 2;
        }

}