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
    const int entries = 128 * 1024^2 / 28;
    TTEntry[] tt = new TTEntry[entries];

    public int positionsEvaluated = 0;
    public int TIME_PER_MOVE = 1000;


    int[] pieceVal = {0, 100, 310, 330, 500, 1000, 10000 };
    int[] piecePhase = {0, 0, 1, 1, 2, 4, 0};
    ulong[] psts = {657614902731556116, 420894446315227099, 384592972471695068, 312245244820264086, 364876803783607569, 
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
        TIME_PER_MOVE = timer.MillisecondsRemaining / 30;
        double alpha = double.NegativeInfinity, beta = double.PositiveInfinity;
        Move bestMove = Move.NullMove;
        double maxEval;
        double aspiration = 50;
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE) {
            //iterative deepening
            (Move bestMoveTemp, maxEval) = NegaMax(board: board, depthLeft: depthLeft, 
                                                depthSoFar: 0, color: 1, alpha: alpha, beta: beta, 
                                                rootIsWhite: board.IsWhiteToMove, prevBestMove: bestMove, timer: timer);
            Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}, in {4} ms\n", 
                bestMoveTemp, maxEval, depthLeft, positionsEvaluated,timer.MillisecondsElapsedThisTurn );
            if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
                break;
            }
            //aspiration window
            if ((maxEval <= alpha || maxEval >= beta) && maxEval > -999999 && maxEval < 999999) { //fail low or high, ignore out of checkmate bounds and draws
                Console.WriteLine("Search failed due to narrow aspiration window, doubling window and trying again");
                alpha -= aspiration;
                beta += aspiration;
                aspiration *= 2;
            }
            else {
                alpha = maxEval - aspiration;
                beta = maxEval + aspiration;
                Console.WriteLine("Aspiration window: [{0}, {1}]", alpha, beta);
                depthLeft++;
                aspiration = 50;
                bestMove = bestMoveTemp;
            }
            
        }
    positionsEvaluated = 0;
    if (bestMove == Move.NullMove) {
        Console.WriteLine("NO LEGAL MOVES");
        return board.GetLegalMoves()[0];
    }
    return bestMove;
    }


public (Move, double) NegaMax(Board board, int depthLeft, int depthSoFar, int color, double alpha, double beta, 
                                bool rootIsWhite, Move prevBestMove, Timer timer)
{
    bool root = depthSoFar == 0;
    if(!root && board.IsRepeatedPosition()) {
            return (Move.NullMove, color * -1);
    }
    Move bestMove = Move.NullMove;
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

        return (bestMove, entry.score);
    }
    if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
        return (bestMove, color * -77777777777);
    }
    if (depthLeft == 0 || board.IsInCheckmate() || board.IsInsufficientMaterial() || 
                board.FiftyMoveCounter >= 100)
    {
        return (bestMove, color * EvaluateBoard(board, rootIsWhite, depthSoFar));
    }
    Span<Move> legalMoves = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref legalMoves, false);
    if (legalMoves.Length  == 0) { //stalemate detected
        return (bestMove, color * -1);
    }
    Span<int> scores = stackalloc int[legalMoves.Length];
    for (int i = 0; i < legalMoves.Length; i++) {
        if (legalMoves[i] == prevBestMove) {
            scores[i] = -999;
        }
        else if (legalMoves[i].IsCapture) {
            scores[i] = (int)legalMoves[i].MovePieceType - 10*(int)legalMoves[i].CapturePieceType; //1 = pawn, 2 = knight, 3 = bishop, 4 = rook, 5 = queen, 6 = king
        }
        else {
            scores[i] = 0;
        }

        }
    MemoryExtensions.Sort(scores, legalMoves);
    double maxEval = double.NegativeInfinity;
    double origAlpha = alpha;
    for (int i =0; i < legalMoves.Length; i++) {
        Move move = legalMoves[i];
        board.MakeMove(move);
        double eval = -NegaMax( board, depthLeft - 1, depthSoFar + 1, 
                             -color, -beta, -alpha,  rootIsWhite, Move.NullMove, timer).Item2;
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
    int bound = maxEval >= beta ? 2 : maxEval > origAlpha ? 3 : 1; // 3 = exact, 2 = lower bound, 1 = upper bound

        // Push to TT
    tt[key % entries] = new TTEntry(key, bestMove, depthLeft, maxEval, bound);
    
    return (bestMove, maxEval);
}
public int GetPstVal(int psq) {
        return (int)(((psts[psq / 10] >> (6 * (psq % 10))) & 63) - 20) * 8;
    }
 public int EvaluateBoard(Board board, bool rootIsWhite, int depthSoFar) {
        positionsEvaluated++;
        int whiteScore = 0, blackScore = 0;
        if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100) {
                    return -1;
                }
        if (board.IsInCheckmate()) {
            (board.IsWhiteToMove ? ref whiteScore : ref blackScore) -= 99999999 - depthSoFar;

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
}