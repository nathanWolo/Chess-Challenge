using ChessChallenge.API;
using System;

public class MyBot : IChessBot
{
    struct TTEntry {
        public ulong key;
        public Move move;
        public int depth, bound;
        public double score;
        public TTEntry(ulong _key, Move _move, int _depth, double _score, int _bound) {
            key = _key; move = _move; depth = _depth; score = _score; bound = _bound;
        }
    }
    const int entries = 128 * 1024^2 / 28; //make sure this is always 128mb for local testing and submission, contest rules say 256mb but i fear the garbage collector
    //its approx 5 million entries, so diminishing returns to make it bigger - not worth the risk of garbage collection putting over the memory limit
    TTEntry[] tt = new TTEntry[entries];

    public int positionsEvaluated = 0;
    public int TIME_PER_MOVE = 1000;
    public int CHECKMATE_SCORE = 9999999;
    Move bestMoveRoot = Move.NullMove;
    /*
    PeSTO's tuned piece tables, from https://www.chessprogramming.org/PeSTO%27s_Evaluation_Function
    Good for now - TODO: run our own tuning on top of this
    */

    readonly int[] pieceVal = {0, 100, 310, 330, 500, 1000, 10000 };
    readonly int[] piecePhase = {0, 0, 1, 1, 2, 4, 0};
    readonly ulong[] psts = {657614902731556116, 420894446315227099, 384592972471695068, 312245244820264086, 364876803783607569, 
    366006824779723922, 366006826859316500, 786039115310605588, 421220596516513823, 366011295806342421, 366006826859316436, 
    366006896669578452, 162218943720801556, 440575073001255824, 657087419459913430, 402634039558223453, 347425219986941203, 365698755348489557, 
    311382605788951956, 147850316371514514, 329107007234708689, 402598430990222677, 402611905376114006, 329415149680141460, 257053881053295759, 291134268204721362, 
    492947507967247313, 367159395376767958, 384021229732455700, 384307098409076181, 402035762391246293, 328847661003244824, 365712019230110867, 366002427738801364, 
    384307168185238804, 347996828560606484, 329692156834174227, 365439338182165780, 386018218798040211, 456959123538409047, 347157285952386452, 365711880701965780, 
    365997890021704981, 221896035722130452, 384289231362147538, 384307167128540502, 366006826859320596, 366006826876093716, 366002360093332756, 366006824694793492, 
    347992428333053139, 457508666683233428, 329723156783776785, 329401687190893908, 366002356855326100, 366288301819245844, 329978030930875600, 420621693221156179, 
    422042614449657239, 384602117564867863, 419505151144195476, 366274972473194070, 329406075454444949, 275354286769374224, 366855645423297932, 329991151972070674, 
    311105941360174354, 256772197720318995, 365993560693875923, 258219435335676691, 383730812414424149, 384601907111998612, 401758895947998613, 420612834953622999, 
    402607438610388375, 329978099633296596, 67159620133902};
    public Move Think(Board board, Timer timer)
    {
        int depthLeft = 1;
        TIME_PER_MOVE = timer.MillisecondsRemaining / 30; //use more time on lichess because of increment
        double alpha = double.NegativeInfinity, beta = double.PositiveInfinity;
        double maxEval;
        double aspiration = 50;
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE) {
            //iterative deepening
            maxEval = NegaMax(board: board, depthLeft: ++depthLeft, 
                                                depthSoFar: 0, alpha: alpha, beta: beta,  timer: timer);
            // Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}, in {4} ms, NPMS: {5}\n", 
            //     bestMoveRoot, maxEval, depthLeft, positionsEvaluated,timer.MillisecondsElapsedThisTurn, positionsEvaluated / (timer.MillisecondsElapsedThisTurn + 1));
            //aspiration window
            if(Math.Abs(maxEval) > (CHECKMATE_SCORE - 100)) break;
            if (maxEval <= alpha || maxEval >= beta) { //fail low or high, ignore out of checkmate bounds and draws
                if (maxEval <= alpha) alpha -= aspiration;
                else beta += aspiration;
                aspiration *= 2;
            }
            else {
                //reset aspiration window
                aspiration = 50;
                alpha = maxEval - aspiration;
                beta = maxEval + aspiration;
            }
            
        }
    positionsEvaluated = 0;
    return bestMoveRoot;
    }


    public double NegaMax(Board board, int depthLeft, int depthSoFar, double alpha, double beta, Timer timer)
    {
        bool inCheck = board.IsInCheck(), root = depthSoFar == 0;
        if(inCheck) depthLeft++; //extend search depth if in check

        bool qsearch = depthLeft <= 0;
        Move bestMove = Move.NullMove;
        if (!root && (board.IsInsufficientMaterial() 
                        || board.IsRepeatedPosition() 
                        || board.FiftyMoveCounter >= 100)) {
            return 0;
        }
        ulong key = board.ZobristKey;
        TTEntry entry = tt[key % entries];
        double maxEval = double.NegativeInfinity;
        if(!root && entry.key == key //verify that the entry is for this position (can very rarely be wrong)
                && entry.depth >= depthLeft //verify that the entry is for a search of at least this depth
                && (entry.bound == 3 // exact score
                    || (entry.bound == 2 && entry.score >= beta )// lower bound, fail high
                    || (entry.bound == 1 && entry.score <= alpha ))) {// upper bound, fail low
            positionsEvaluated++;
            return entry.score;
        }
        //reverse futility pruning
        //Basic idea: if your score is so good you can take a big hit and still get the beta cutoff, go for it.
        int stand_pat = EvaluateBoard(board);
        if(qsearch) {
            maxEval = stand_pat;
            if(maxEval >= beta) return maxEval;
            alpha = Math.Max(alpha, maxEval);
        }
        if (stand_pat - 150 * depthLeft >= beta  //TODO: tune this constant
            && !qsearch ) {//dont prune in qsearch
            return beta; //fail hard, TODO: try fail soft
        }
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
            3. promotions
            4. Other
            */
            scores[i] = (legalMoves[i] == entry.move && entry.key == key) ? -999 : //TT move
                legalMoves[i].IsCapture ? (int)legalMoves[i].MovePieceType - 10 * (int)legalMoves[i].CapturePieceType : //MVV/LVA
                legalMoves[i].IsPromotion ? -2 : 0; //promotions
            }
        MemoryExtensions.Sort(scores, legalMoves);


        double origAlpha = alpha;
        for (int i =0; i < legalMoves.Length; i++) {
            if (timer.MillisecondsElapsedThisTurn > TIME_PER_MOVE) return -CHECKMATE_SCORE;
            Move move = legalMoves[i];
            board.MakeMove(move);
            double eval = -NegaMax( board, depthLeft - 1, depthSoFar + 1, 
                                -beta, -alpha, timer);
            board.UndoMove(move);
            if (eval > maxEval)
            {
                maxEval = eval;
                bestMove = move;
                if (root && maxEval < beta && maxEval > origAlpha) bestMoveRoot = move;
            }
            alpha = Math.Max(alpha, maxEval);
            if (alpha >= beta) break;
        }
        //important to know if this is an exact score or just a lower/upper bound
        int bound = maxEval >= beta ? 2 : maxEval > origAlpha ? 3 : 1; // 3 = exact, 2 = lower bound, 1 = upper bound

        // Push to TT
        tt[key % entries] = new TTEntry(key, bestMove, depthLeft, maxEval, bound);
        
        return maxEval;
    }



    public int GetPstVal(int psq) {
            //black magic bit sorcery
            return (int)(((psts[psq / 10] >> (6 * (psq % 10))) & 63) - 20) * 8;
        }


    public int EvaluateBoard(Board board) {
            positionsEvaluated++;
            
            int mg = 0, eg = 0, phase = 0;

            foreach(bool sideToMove in new[] {true, false}) { //true = white, false = black
                for(var p = PieceType.Pawn; p <= PieceType.King; p++) {
                    int piece = (int)p, ind;
                    ulong mask = board.GetPieceBitboard(p, sideToMove);
                    while(mask != 0) {
                        phase += piecePhase[piece];
                        ind = 128 * (piece - 1) + BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^ (sideToMove ? 56 : 0);
                        mg += GetPstVal(ind) + pieceVal[piece];
                        eg += GetPstVal(ind + 64) + pieceVal[piece];
                    }
                }

                mg = -mg;
                eg = -eg;
            }
            // mg represents whites midgame score - blacks midgame score
            // eg represents whites endgame score - blacks endgame score

            int overallScore = (mg * phase + eg * (24 - phase)) / 24;
            return board.IsWhiteToMove ? overallScore : -overallScore;

        }

}