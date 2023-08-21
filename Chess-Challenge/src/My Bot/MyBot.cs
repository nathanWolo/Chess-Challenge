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
    public int OUT_OF_TIME_SCORE = 77777;
    public int CHECKMATE_SCORE = 9999999;
    public int MAX_DEPTH = 50;
    /*
    PeSTO's tuned piece tables, from https://www.chessprogramming.org/PeSTO%27s_Evaluation_Function
    Good for now - TODO: run our own tuning or perhaps use a neural network
    Maybe genetic algorithm to tune the piece tables?
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
        Move bestMove = Move.NullMove;
        double maxEval;
        double aspiration = 50;
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE && depthLeft < MAX_DEPTH) {
            //iterative deepening
            (Move bestMoveTemp, maxEval) = NegaMax(board: board, depthLeft: ++depthLeft, 
                                                depthSoFar: 0, color: 1, alpha: alpha, beta: beta, 
                                                rootIsWhite: board.IsWhiteToMove, timer: timer);
            // Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}, in {4} ms, NPMS: {5}\n", 
            //     bestMoveTemp, maxEval, depthLeft, positionsEvaluated,timer.MillisecondsElapsedThisTurn, positionsEvaluated / (timer.MillisecondsElapsedThisTurn + 1));
            if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
                break;
            }
            //aspiration window
            if (maxEval <= alpha || maxEval >= beta) { //fail low or high, ignore out of checkmate bounds and draws
                // Console.WriteLine("Search failed due to narrow aspiration window, doubling window and trying again");
                if (maxEval <= alpha) alpha -= aspiration;
                else beta += aspiration;
                aspiration *= 2;
            }
            else {
                //reset aspiration window
                alpha = maxEval - aspiration;
                beta = maxEval + aspiration;
                aspiration = 50;
                bestMove = bestMoveTemp;
            }
            // Console.WriteLine("Aspiration window: [{0}, {1}]", alpha, beta);
            
        }
    positionsEvaluated = 0;
    return bestMove;
    }


    public (Move, double) NegaMax(Board board, int depthLeft, int depthSoFar, int color, double alpha, double beta, 
                                bool rootIsWhite, Timer timer)
    {
        bool root = depthSoFar == 0;
        Move bestMove = Move.NullMove;
        if(!root && board.IsRepeatedPosition()) {
                return (bestMove, 0);
        }
        ulong key = board.ZobristKey;
        TTEntry entry = tt[key % entries];

        if(!root && entry.key == key && entry.depth >= depthLeft && (
                entry.bound == 3 // exact score
                    || (entry.bound == 2 && entry.score >= beta )// lower bound, fail high
                    || (entry.bound == 1 && entry.score <= alpha )// upper bound, fail low
            )) {
    ///TODO: implement bound narrowing (source: wikipedia)
    ///    ttEntry := transpositionTableLookup(node)
        // if ttEntry is valid and ttEntry.depth ≥ depth then
        //     if ttEntry.flag = EXACT then
        //         return ttEntry.value
        //     else if ttEntry.flag = LOWERBOUND then
        //         α := max(α, ttEntry.value)
        //     else if ttEntry.flag = UPPERBOUND then
        //         β := min(β, ttEntry.value)

        //     if α ≥ β then
        //         return ttEntry.value
            positionsEvaluated++;
            return (bestMove, entry.score);
        }
        if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
            return (bestMove, color * -OUT_OF_TIME_SCORE);
        }
        if (board.IsInCheckmate() || board.IsInsufficientMaterial() || 
                    board.FiftyMoveCounter >= 100)
        {
            return (bestMove, color * EvaluateBoard(board, rootIsWhite, depthSoFar));
        }
        if (depthLeft == 0) {
            return (bestMove, Quiesce(board, alpha, beta, depthSoFar, rootIsWhite, timer, color));
        }
            //reverse futility pruning

        /*
        Basic idea: if your score is so good you can take a big hit and still get the beta cutoff, go for it.
        Pseudocode from talkchess.com:

            if &#40;depth == 1 &&
            Eval&#40;) - VALUE_BISHOP > beta&#41;
            return beta;

        if &#40;depth == 2 &&
                Eval&#40;) - VALUE_ROOK > beta&#41;
            return beta;

        if &#40;depth == 3 &&
            Eval&#40;) - VALUE_QUEEN > beta&#41;
            depth--;
        */
        int stand_pat = color * EvaluateBoard(board, rootIsWhite, depthSoFar);
        if (stand_pat - 200 * depthLeft >= beta) { //TODO: tune this constant
            return (bestMove, beta);
        }
        Span<Move> legalMoves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref legalMoves, false);

            // int whiteScore = 0, blackScore = 0;
            // if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100) {
            //             return -1;
            //         }
            // if (board.IsInCheckmate()) {
            //     (board.IsWhiteToMove ? ref whiteScore : ref blackScore) -= CHECKMATE_SCORE - depthSoFar;

            //     return rootIsWhite ? whiteScore - blackScore : blackScore - whiteScore;

            // }
        if (legalMoves.Length  == 0) { //stalemate detected
            return (bestMove, color * -1);
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
            scores[i] = (legalMoves[i] == entry.move && entry.key == key) ? -999 :
                legalMoves[i].IsCapture ? (int)legalMoves[i].MovePieceType - 10 * (int)legalMoves[i].CapturePieceType :
                legalMoves[i].IsPromotion ? -2 : 0;
            }
        MemoryExtensions.Sort(scores, legalMoves);

        double maxEval = double.NegativeInfinity;
        double origAlpha = alpha;
        for (int i =0; i < legalMoves.Length; i++) {
            Move move = legalMoves[i];
            board.MakeMove(move);
            double eval = -NegaMax( board, depthLeft - 1, depthSoFar + 1, 
                                -color, -beta, -alpha,  rootIsWhite, timer).Item2;
            board.UndoMove(move);
            if (eval > maxEval)
            {
                maxEval = eval;
                bestMove = move;
            }
            alpha = Math.Max(alpha, maxEval);
            if (alpha >= beta) {
                break;
            }
        }
        //important to know if this is an exact score or just a lower/upper bound
        int bound = maxEval >= beta ? 2 : maxEval > origAlpha ? 3 : 1; // 3 = exact, 2 = lower bound, 1 = upper bound

        // Push to TT
        tt[key % entries] = new TTEntry(key, bestMove, depthLeft, maxEval, bound);
        
        return (bestMove, maxEval);
    }
    public int GetPstVal(int psq) {
            //black magic bit sorcery
            return (int)(((psts[psq / 10] >> (6 * (psq % 10))) & 63) - 20) * 8;
        }
    public int EvaluateBoard(Board board, bool rootIsWhite, int depthSoFar) {
            positionsEvaluated++;
            int whiteScore = 0, blackScore = 0;
            if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100) {
                        return -1;
                    }
            if (board.IsInCheckmate()) {
                (board.IsWhiteToMove ? ref whiteScore : ref blackScore) -= CHECKMATE_SCORE - depthSoFar;

                return rootIsWhite ? whiteScore - blackScore : blackScore - whiteScore;

            }
            
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

            return rootIsWhite ? overallScore : -overallScore;

        }


    /*
    Wikipedia Qsearch pseudocode: 

    int Quiesce( int alpha, int beta ) {
        int stand_pat = Evaluate();
        if( stand_pat >= beta )
            return beta;
        if( alpha < stand_pat )
            alpha = stand_pat;

        until( every_capture_has_been_examined )  {
            MakeCapture();
            score = -Quiesce( -beta, -alpha );
            TakeBackMove();

            if( score >= beta )
                return beta;
            if( score > alpha )
            alpha = score;
        }
        return alpha;
    }

    */

    public double Quiesce(Board board, double alpha, double beta, int depthSoFar, bool rootIsWhite, Timer timer, int color) {

        if (timer.MillisecondsElapsedThisTurn > TIME_PER_MOVE) {
            return -OUT_OF_TIME_SCORE;
        }
        ulong key = board.ZobristKey;
        TTEntry entry = tt[key % entries];

        if(entry.key == key && entry.bound == 3) {
            positionsEvaluated++;
            return entry.score;
            }
        int stand_pat = color * EvaluateBoard(board, rootIsWhite, depthSoFar);

        if (stand_pat >= beta) {
            return beta;
        }
        if (alpha < stand_pat) {
            alpha = stand_pat;
        }

        Span<Move> legalMoves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref legalMoves, true);
        Span<int> scores = stackalloc int[legalMoves.Length];
        for (int j = 0; j < legalMoves.Length; j++) {
            if (legalMoves[j].IsCapture) {
                scores[j] = (int)legalMoves[j].MovePieceType - 10*(int)legalMoves[j].CapturePieceType; //1 = pawn, 2 = knight, 3 = bishop, 4 = rook, 5 = queen, 6 = king
            }
            else {
                scores[j] = 0;
            }

            }
        MemoryExtensions.Sort(scores, legalMoves);
        int i = 0;
        while(i < legalMoves.Length) {
            Move move = legalMoves[i];
            board.MakeMove(move);
            double score = -Quiesce(board, -beta, -alpha, depthSoFar + 1, rootIsWhite, timer, -color);
            board.UndoMove(move);
            if (score >= beta) {
                return beta;
            }
            if (score > alpha) {
                alpha = score;
            }
            i++;
        }
        return alpha;

        }

}