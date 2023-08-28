using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static System.Math;

namespace ChessChallenge.Example
{
public class Onion41 : IChessBot
{
    struct TTEntry {
        public ulong key; //64 bits
        public Move move; //
        public int depth, bound, score; //32 bits each
        public TTEntry(ulong _key, Move _move, int _depth, int _score, int _bound) {
            key = _key; move = _move; depth = _depth; score = _score; bound = _bound;
        }
    }
    const int entries = 128 * 1024^2 / 28; //make sure this is always 128mb for local testing and submission, contest rules say 256mb but i fear the garbage collector
    //its approx 5 million entries, so diminishing returns to make it bigger - not worth the risk of garbage collection putting over the memory limit
    TTEntry[] tt = new TTEntry[entries];

    int[,,] historyTable = new int[2, 7, 64];

    // public int positionsEvaluated = 0;
    public int TIME_PER_MOVE;
    public int CHECKMATE_SCORE = 9999999;
    Move bestMoveRoot;
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
        
        TIME_PER_MOVE = timer.MillisecondsRemaining / 30; //use more time on lichess because of increment
        Array.Clear(historyTable, 0, historyTable.Length); //reset history table
        for (int depthLeft = 1, alpha = -CHECKMATE_SCORE, beta = CHECKMATE_SCORE, maxEval ;;) {
            //iterative deepening
            maxEval = NegaMax(board, depthLeft, 0, alpha, beta, timer);
            // Console.Write("best move: {0}, value: {1}, depth: {2}\n", bestMoveRoot, maxEval, depthLeft);
            if (Abs(maxEval) > CHECKMATE_SCORE - 100 || depthLeft > 30) return bestMoveRoot; //ran out of time
            //aspiration window
            if (maxEval <= alpha || maxEval >= beta) { //fail low or high
                if (maxEval <= alpha) alpha -= 15;
                else beta += 15;
            }
            else {
                //reset aspiration window
                alpha = maxEval - 15;
                beta = maxEval + 15;
                depthLeft++;
            }
        }
    }


    public int NegaMax(Board board, int depthLeft, int depthSoFar, int alpha, int beta, Timer timer)
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
        int maxEval = -CHECKMATE_SCORE;
        if(!root && entry.key == key //verify that the entry is for this position (can very rarely be wrong)
                && entry.depth >= depthLeft //verify that the entry is for a search of at least this depth
                && (entry.bound == 3 // exact score
                    || (entry.bound == 2 && entry.score >= beta )// lower bound, fail high
                    || (entry.bound == 1 && entry.score <= alpha ))) {// upper bound, fail low
            // positionsEvaluated++;
            return entry.score;
        }


        //reverse futility pruning
        //Basic idea: if your score is so good you can take a big hit and still get the beta cutoff, go for it.
        int standPat = EvaluateBoard(board);
        if(qsearch) {
            maxEval = standPat;
            if(maxEval >= beta) return maxEval;
            alpha = Max(alpha, maxEval);
        }

        if (standPat - 150 * depthLeft >= beta  //TODO: tune this constant. Note, started at 150. Binary search [75, 150] to find best value
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
            3. history heuristic
            */
            scores[i] = (legalMoves[i] == entry.move && entry.key == key) ? -999999 : //TT move
                legalMoves[i].IsCapture ? (int)legalMoves[i].MovePieceType - 1000 * (int)legalMoves[i].CapturePieceType : //MVV/LVA
                historyTable[depthSoFar & 1, (int)legalMoves[i].MovePieceType, legalMoves[i].TargetSquare.Index]; //history heuristic
        }
        MemoryExtensions.Sort(scores, legalMoves);


        double origAlpha = alpha;
        for (int i =0; i < legalMoves.Length; i++) {
            if (timer.MillisecondsElapsedThisTurn > TIME_PER_MOVE) return -CHECKMATE_SCORE; //ran out of time
            
            if(canPruneMove && scores[i] == 0 && i > 0) continue; //prune move if it cant raise alpha, not a tactical move, and not the first move
            
            Move move = legalMoves[i];
            board.MakeMove(move);
            int eval = -NegaMax( board, depthLeft - 1, depthSoFar + 1, -beta, -alpha, timer);
            board.UndoMove(move);

            if (eval > maxEval)
            {
                maxEval = eval;
                bestMove = move;
                if (root && maxEval < beta && maxEval > origAlpha) 
                    bestMoveRoot = move; //is verifying the bounds here actually needed?
            }

            alpha = Max(alpha, maxEval);

            if (alpha >= beta){
                //history heuristic

                if (!move.IsCapture) 
                    //dont update history for captures
                    historyTable[depthSoFar & 1, (int)move.MovePieceType, move.TargetSquare.Index] -= depthLeft * depthLeft;
                
                break;
            }
        }
        //important to know if this is an exact score or just a lower/upper bound
        int bound = maxEval >= beta ? 2 : maxEval > origAlpha ? 3 : 1; // 3 = exact, 2 = lower bound, 1 = upper bound

        // Push to TT
        tt[key % entries] = new TTEntry(key, bestMove, depthLeft, maxEval, bound);
        
        return maxEval;
    }


    //evaluation code shamelessly stolen from JW's example chess engine
    //TODO: try and improve this eventually
    public int GetPstVal(int psq) {
            //black magic bit sorcery
            return (int)(((psts[psq / 10] >> (6 * (psq % 10))) & 63) - 20) * 8;
        }


    public int EvaluateBoard(Board board) {
            // positionsEvaluated++;
            
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

public class Onion4 : IChessBot
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
        double alpha = double.NegativeInfinity, beta = double.PositiveInfinity, maxEval = 0;
        double aspiration = 15;
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE && (Math.Abs(maxEval) < (CHECKMATE_SCORE - 100))) {
            //iterative deepening
            maxEval = NegaMax(board: board, depthLeft: depthLeft, 
                                                depthSoFar: 0, alpha: alpha, beta: beta,  timer: timer);
            // Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}, in {4} ms, NPMS: {5}\n", 
            //     bestMoveRoot, maxEval, depthLeft, positionsEvaluated,timer.MillisecondsElapsedThisTurn, positionsEvaluated / (timer.MillisecondsElapsedThisTurn + 1));
            //aspiration window
            if (maxEval <= alpha || maxEval >= beta) { //fail low or high
                if (maxEval <= alpha) alpha -= aspiration;
                else beta += aspiration;
                aspiration *= 1.5;
            }
            else {
                //reset aspiration window
                aspiration = 15;
                alpha = maxEval - aspiration;
                beta = maxEval + aspiration;
                depthLeft++;
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
        int standPat = EvaluateBoard(board);
        if(qsearch) {
            maxEval = standPat;
            if(maxEval >= beta) return maxEval;
            alpha = Math.Max(alpha, maxEval);
        }
        if (standPat - 150 * depthLeft >= beta  //TODO: tune this constant
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
            if(canPruneMove && scores[i] == 0 && i > 0) continue; //prune move
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


    //evaluation code shamelessly stolen from JW's example chess engine
    //TODO: try and improve this eventually
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
public class MyQsearchV12 : IChessBot
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
        int standPat = EvaluateBoard(board);
        if(qsearch) {
            maxEval = standPat;
            if(maxEval >= beta) return maxEval;
            alpha = Math.Max(alpha, maxEval);
        }
        if (standPat - 150 * depthLeft >= beta  //TODO: tune this constant
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
            if(canPruneMove && scores[i] == 0 && i > 0) continue; //prune move
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


    //evaluation code shamelessly stolen from JW's example chess engine
    //TODO: try and improve this eventually
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


public class MyQsearchV11 : IChessBot
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

public class MyQsearchV10 : IChessBot
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
    public int MAX_DEPTH = 30;
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
    Move bestMoveRoot = Move.NullMove;
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
            maxEval = NegaMax(board: board, depthLeft: ++depthLeft, 
                                                depthSoFar: 0, color: 1, alpha: alpha, beta: beta, 
                                                rootIsWhite: board.IsWhiteToMove, timer: timer);
            // Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}, in {4} ms, NPMS: {5}\n", 
            //     bestMoveRoot, maxEval, depthLeft, positionsEvaluated,timer.MillisecondsElapsedThisTurn, positionsEvaluated / (timer.MillisecondsElapsedThisTurn + 1));
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
                aspiration = 50; //SHOULD THIS GET RESET FIRST?? TODO: test
            }
            // Console.WriteLine("Aspiration window: [{0}, {1}]", alpha, beta);
            
        }
    positionsEvaluated = 0;
    return bestMoveRoot;
    }


    public double NegaMax(Board board, int depthLeft, int depthSoFar, int color, double alpha, double beta, 
                                bool rootIsWhite, Timer timer)
    {
        bool root = depthSoFar == 0;
        Move bestMove = Move.NullMove;
        if (!root && (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100)) {
                        return 0;
                    }
        ulong key = board.ZobristKey;
        TTEntry entry = tt[key % entries];
        double maxEval = double.NegativeInfinity;
        if(!root && entry.key == key && entry.depth >= depthLeft && (
                entry.bound == 3 // exact score
                    || (entry.bound == 2 && entry.score >= beta )// lower bound, fail high
                    || (entry.bound == 1 && entry.score <= alpha )// upper bound, fail low
            )) {
            positionsEvaluated++;
            return entry.score;
        }
        if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
            return color * -OUT_OF_TIME_SCORE;
        }
        //reverse futility pruning
        //Basic idea: if your score is so good you can take a big hit and still get the beta cutoff, go for it.
        int stand_pat = color * EvaluateBoard(board, rootIsWhite, depthSoFar);
        bool qsearch = depthLeft <= 0;
        if(qsearch) {
            maxEval = stand_pat;
            if(maxEval >= beta) return maxEval;
            alpha = Math.Max(alpha, maxEval);
        }
        if (Math.Abs(stand_pat) >= (CHECKMATE_SCORE - 100)) {
            return  stand_pat;
        }
        if (stand_pat - 150 * depthLeft >= beta && !qsearch) { //TODO: tune this constant
            return  beta; //fail hard, TODO: try fail soft
        }
        Span<Move> legalMoves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref legalMoves, qsearch);
        if (legalMoves.Length  == 0 && !qsearch) { //stalemate detected
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
            scores[i] = (legalMoves[i] == entry.move && entry.key == key) ? -999 :
                legalMoves[i].IsCapture ? (int)legalMoves[i].MovePieceType - 10 * (int)legalMoves[i].CapturePieceType :
                legalMoves[i].IsPromotion ? -2 : 0;
            }
        MemoryExtensions.Sort(scores, legalMoves);
        double origAlpha = alpha;
        for (int i =0; i < legalMoves.Length; i++) {
            Move move = legalMoves[i];
            board.MakeMove(move);
            double eval = -NegaMax( board, depthLeft - 1, depthSoFar + 1, 
                                -color, -beta, -alpha,  rootIsWhite, timer);
            board.UndoMove(move);
            if (eval > maxEval)
            {
                maxEval = eval;
                bestMove = move;
                if (root) bestMoveRoot = move;
            }
            alpha = Math.Max(alpha, maxEval);
            if (alpha >= beta)  break;
            
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
    public int EvaluateBoard(Board board, bool rootIsWhite, int depthSoFar) {
            positionsEvaluated++;
            int whiteScore = 0, blackScore = 0;
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

}
public class MyQsearchV9 : IChessBot
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
    public int MAX_DEPTH = 30;
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
        if (!root && (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100)) {
                        return (bestMove, 0);
                    }
        ulong key = board.ZobristKey;
        TTEntry entry = tt[key % entries];
        double maxEval = double.NegativeInfinity;
        if(!root && entry.key == key && entry.depth >= depthLeft && (
                entry.bound == 3 // exact score
                    || (entry.bound == 2 && entry.score >= beta )// lower bound, fail high
                    || (entry.bound == 1 && entry.score <= alpha )// upper bound, fail low
            )) {
            positionsEvaluated++;
            return (bestMove, entry.score);
        }
        if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
            return (bestMove, color * -OUT_OF_TIME_SCORE);
        }
        //reverse futility pruning
        //Basic idea: if your score is so good you can take a big hit and still get the beta cutoff, go for it.
        int stand_pat = color * EvaluateBoard(board, rootIsWhite, depthSoFar);
        bool qsearch = depthLeft <= 0;
        if(qsearch) {
            maxEval = stand_pat;
            if(maxEval >= beta) return (bestMove, maxEval);
            alpha = Math.Max(alpha, maxEval);
        }
        if (Math.Abs(stand_pat) >= (CHECKMATE_SCORE - 100)) {
            return (bestMove, stand_pat);
        }
        if (stand_pat - 150 * depthLeft >= beta && !qsearch) { //TODO: tune this constant
            return (bestMove, beta); //fail hard, TODO: try fail soft
        }
        Span<Move> legalMoves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref legalMoves, qsearch);
        if (legalMoves.Length  == 0 && !qsearch) { //stalemate detected
            return (bestMove, 0);
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

}
public class MyQsearchV8 : IChessBot
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
        if (!root && (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100)) {
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
        // if (board.IsInCheckmate() || board.IsInsufficientMaterial() || 
        //             board.FiftyMoveCounter >= 100)
        // {
        //     return (bestMove, color * EvaluateBoard(board, rootIsWhite, depthSoFar));
        // }
        if (depthLeft == 0) {
            return (bestMove, Quiesce(board, alpha, beta, depthSoFar, rootIsWhite, timer, color));
        }
        //reverse futility pruning
        //Basic idea: if your score is so good you can take a big hit and still get the beta cutoff, go for it.
        int stand_pat = color * EvaluateBoard(board, rootIsWhite, depthSoFar);
        if (Math.Abs(stand_pat) >= (CHECKMATE_SCORE - 100)) {
            return (bestMove, stand_pat);
        }
        if (stand_pat - 150 * depthLeft >= beta) { //TODO: tune this constant
            return (bestMove, beta); //fail hard, TODO: try fail soft
        }
        Span<Move> legalMoves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref legalMoves, false);
        if (legalMoves.Length  == 0) { //stalemate detected
            return (bestMove, 0);
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
                scores[j] = (int)legalMoves[j].MovePieceType - 10*(int)legalMoves[j].CapturePieceType; //1 = pawn, 2 = knight, 3 = bishop, 4 = rook, 5 = queen, 6 = king
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



public class MyQsearchV7 : IChessBot
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
public class MyQsearchV6 : IChessBot
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
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE) {
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
    if (bestMove == Move.NullMove) {
        // Console.WriteLine("NO LEGAL MOVES");
        return board.GetLegalMoves()[0];
    }
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
        return (entry.move, entry.score);
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
    // int stand_pat = color * EvaluateBoard(board, rootIsWhite, depthSoFar);
    // if (stand_pat - 200 * depthLeft >= beta) { //TODO: tune this constant
    //     return (bestMove, stand_pat - 200 * depthLeft);
    // }
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

    public class MyQsearchV5 : IChessBot
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
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE) {
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
    if (bestMove == Move.NullMove) {
        // Console.WriteLine("NO LEGAL MOVES");
        return board.GetLegalMoves()[0];
    }
    return bestMove;
    }


public (Move, double) NegaMax(Board board, int depthLeft, int depthSoFar, int color, double alpha, double beta, 
                                bool rootIsWhite, Timer timer)
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
    // int stand_pat = color * EvaluateBoard(board, rootIsWhite, depthSoFar);
    // if (stand_pat - 200 * depthLeft >= beta) { //TODO: tune this constant
    //     return (bestMove, stand_pat - 200 * depthLeft);
    // }
    Span<Move> legalMoves = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref legalMoves, false);
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
public class MyQsearchV4 : IChessBot
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
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE) {
            //iterative deepening
            (Move bestMoveTemp, maxEval) = NegaMax(board: board, depthLeft: ++depthLeft, 
                                                depthSoFar: 0, color: 1, alpha: alpha, beta: beta, 
                                                rootIsWhite: board.IsWhiteToMove, prevBestMove: bestMove, timer: timer);
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
    if (bestMove == Move.NullMove) {
        // Console.WriteLine("NO LEGAL MOVES");
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
    // int stand_pat = color * EvaluateBoard(board, rootIsWhite, depthSoFar);
    // if (stand_pat - 200 * depthLeft >= beta) { //TODO: tune this constant
    //     return (bestMove, stand_pat - 200 * depthLeft);
    // }
    Span<Move> legalMoves = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref legalMoves, false);
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
public class MyQsearchV3 : IChessBot
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
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE) {
            //iterative deepening
            (Move bestMoveTemp, maxEval) = NegaMax(board: board, depthLeft: ++depthLeft, 
                                                depthSoFar: 0, color: 1, alpha: alpha, beta: beta, 
                                                rootIsWhite: board.IsWhiteToMove, prevBestMove: bestMove, timer: timer);
            // Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}, in {4} ms, NPMS: {5}\n", 
            //     bestMoveTemp, maxEval, depthLeft, positionsEvaluated,timer.MillisecondsElapsedThisTurn, positionsEvaluated / (timer.MillisecondsElapsedThisTurn + 1));
            if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
                break;
            }
            //aspiration window
            if ((maxEval <= alpha || maxEval >= beta) && maxEval > -CHECKMATE_SCORE && maxEval < CHECKMATE_SCORE) { //fail low or high, ignore out of checkmate bounds and draws
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
    if (bestMove == Move.NullMove) {
        // Console.WriteLine("NO LEGAL MOVES");
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
    Span<Move> legalMoves = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref legalMoves, false);
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

public class MyQsearchV2 : IChessBot
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
    const int entries = 128 * 1024^2 / 28; //cheating for lichess, revert to 128 * 1024^2 / 28 for local testing
    TTEntry[] tt = new TTEntry[entries];

    public int positionsEvaluated = 0;
    public int TIME_PER_MOVE = 1000;
    public int OUT_OF_TIME_SCORE = 77777;
    public int CHECKMATE_SCORE = 9999999;


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
        TIME_PER_MOVE = timer.MillisecondsRemaining / 30; //use more time on lichess because of increment
        double alpha = double.NegativeInfinity, beta = double.PositiveInfinity;
        Move bestMove = Move.NullMove;
        double maxEval;
        double aspiration = 50;
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE) {
            //iterative deepening
            (Move bestMoveTemp, maxEval) = NegaMax(board: board, depthLeft: depthLeft, 
                                                depthSoFar: 0, color: 1, alpha: alpha, beta: beta, 
                                                rootIsWhite: board.IsWhiteToMove, prevBestMove: bestMove, timer: timer);
            // Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}, in {4} ms\n", 
            //     bestMoveTemp, maxEval, depthLeft, positionsEvaluated,timer.MillisecondsElapsedThisTurn );
            if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
                break;
            }
            //aspiration window
            if ((maxEval <= alpha || maxEval >= beta) && maxEval > -CHECKMATE_SCORE && maxEval < CHECKMATE_SCORE) { //fail low or high, ignore out of checkmate bounds and draws
                // Console.WriteLine("Search failed due to narrow aspiration window, doubling window and trying again");
                if (maxEval <= alpha) alpha -= aspiration;
                else beta += aspiration;
                // alpha -= aspiration;
                // beta += aspiration;
                aspiration *= 2;
            }
            else {
                alpha = maxEval - aspiration;
                beta = maxEval + aspiration;
                // Console.WriteLine("Aspiration window: [{0}, {1}]", alpha, beta);
                depthLeft++;
                aspiration = 50;
                bestMove = bestMoveTemp;
            }
            
        }
    positionsEvaluated = 0;
    if (bestMove == Move.NullMove) {
        // Console.WriteLine("NO LEGAL MOVES");
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
public class MyQsearchV1 : IChessBot
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
    const int entries = 128 * 1024^2 / 28; //cheating for lichess, revert to 128 * 1024^2 / 28 for local testing
    TTEntry[] tt = new TTEntry[entries];

    public int positionsEvaluated = 0;
    public int TIME_PER_MOVE = 1000;
    public int OUT_OF_TIME_SCORE = 77777;
    public int CHECKMATE_SCORE = 9999999;


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
        TIME_PER_MOVE = timer.MillisecondsRemaining / 30; //use more time on lichess because of increment
        double alpha = double.NegativeInfinity, beta = double.PositiveInfinity;
        Move bestMove = Move.NullMove;
        double maxEval;
        double aspiration = 50;
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE) {
            //iterative deepening
            (Move bestMoveTemp, maxEval) = NegaMax(board: board, depthLeft: depthLeft, 
                                                depthSoFar: 0, color: 1, alpha: alpha, beta: beta, 
                                                rootIsWhite: board.IsWhiteToMove, prevBestMove: bestMove, timer: timer);
            // Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}, in {4} ms\n", 
            //     bestMoveTemp, maxEval, depthLeft, positionsEvaluated,timer.MillisecondsElapsedThisTurn );
            if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
                break;
            }
            //aspiration window
            if ((maxEval <= alpha || maxEval >= beta) && maxEval > -CHECKMATE_SCORE && maxEval < CHECKMATE_SCORE) { //fail low or high, ignore out of checkmate bounds and draws
                // Console.WriteLine("Search failed due to narrow aspiration window, doubling window and trying again");
                if (maxEval <= alpha) alpha -= aspiration;
                else beta += aspiration;
                // alpha -= aspiration;
                // beta += aspiration;
                aspiration *= 2;
            }
            else {
                alpha = maxEval - aspiration;
                beta = maxEval + aspiration;
                // Console.WriteLine("Aspiration window: [{0}, {1}]", alpha, beta);
                depthLeft++;
                aspiration = 50;
                bestMove = bestMoveTemp;
            }
            
        }
    positionsEvaluated = 0;
    if (bestMove == Move.NullMove) {
        // Console.WriteLine("NO LEGAL MOVES");
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

//TODO: IMPLEMENT QUIESCENCE SEARCH

/*
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
    int stand_pat = color * EvaluateBoard(board, rootIsWhite, depthSoFar);

    if (stand_pat >= beta) {
        return beta;
    }
    if (alpha < stand_pat) {
        alpha = stand_pat;
    }

    Span<Move> legalMoves = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref legalMoves, true);
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




public class MyPackedEvalV3 : IChessBot
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
            // Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}, in {4} ms\n", 
            //     bestMoveTemp, maxEval, depthLeft, positionsEvaluated,timer.MillisecondsElapsedThisTurn );
            if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
                break;
            }
            //aspiration window
            if ((maxEval <= alpha || maxEval >= beta) && maxEval > -999999 && maxEval < 999999) { //fail low or high, ignore out of checkmate bounds and draws
                // Console.WriteLine("Search failed due to narrow aspiration window, doubling window and trying again");
                alpha -= aspiration;
                beta += aspiration;
                aspiration *= 2;
            }
            else {
                alpha = maxEval - aspiration;
                beta = maxEval + aspiration;
                // Console.WriteLine("Aspiration window: [{0}, {1}]", alpha, beta);
                depthLeft++;
                aspiration = 50;
                bestMove = bestMoveTemp;
            }
            
        }
    positionsEvaluated = 0;
    if (bestMove == Move.NullMove) {
        // Console.WriteLine("NO LEGAL MOVES");
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

public class MyPackedEvalV2 : IChessBot
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
        double alpha = double.NegativeInfinity;
        double beta = double.PositiveInfinity;
        Move bestMove = Move.NullMove;
        double maxEval;
        Move bestMoveTemp = Move.NullMove;
        double aspiration = 50;
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE) {
            //iterative deepening
            (bestMoveTemp, maxEval) = NegaMax(board: board, depthLeft: depthLeft, 
                                                depthSoFar: 0, color: 1, alpha: alpha, beta: beta, 
                                                rootIsWhite: board.IsWhiteToMove, prevBestMove: bestMove, timer: timer);
            // Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}, in {4} ms\n", 
            //     bestMoveTemp, maxEval, depthLeft, positionsEvaluated,timer.MillisecondsElapsedThisTurn );
            if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
                break;
            }
            //aspiration window
            if ((maxEval <= alpha || maxEval >= beta) && maxEval > -999999 && maxEval < 999999) { //fail low or high, ignore out of checkmate bounds and draws
                //Console.WriteLine("Search failed due to narrow aspiration window, doubling window and trying again");
                alpha -= aspiration;
                beta += aspiration;
                aspiration *= 2;
            }
            else {
                alpha = maxEval - aspiration;
                beta = maxEval + aspiration;
                //Console.WriteLine("Aspiration window: [{0}, {1}]", alpha, beta);
                depthLeft++;
                aspiration = 50;
                bestMove = bestMoveTemp;
            }
            
        }
    positionsEvaluated = 0;
    return bestMove;
    }
    public int GetNumPieces(Board board) {
        int numPieces = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        foreach (PieceList pieceList in pieces) {
            numPieces += pieceList.Count;
        }
        return numPieces;
    }


public (Move, double) NegaMax(Board board, int depthLeft, int depthSoFar, int color, double alpha, double beta, 
                                bool rootIsWhite, Move prevBestMove, Timer timer)
{
    bool root = depthSoFar == 0;
    if(!root && board.IsRepeatedPosition()) {
            return (Move.NullMove, color * -1);
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

        return (Move.NullMove, entry.score);
    }
    if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
        return (Move.NullMove, color * -77777777777);
    }
    if (depthLeft == 0 || board.IsInCheckmate() || board.IsInsufficientMaterial() || 
                board.FiftyMoveCounter >= 100)
    {
        return (Move.NullMove, color * EvaluateBoard(board, rootIsWhite, depthSoFar));
    }
    System.Span<Move> legalMoves = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref legalMoves, false);
    if (legalMoves.Length  == 0) { //stalemate detected
        return (Move.NullMove, color * -1);
    }
    System.Span<int> scores = stackalloc int[legalMoves.Length];
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
    System.MemoryExtensions.Sort(scores, legalMoves);
    double maxEval = double.NegativeInfinity;
    Move bestMove = Move.NullMove;
    double origAlpha = alpha;
    for (int i =0; i < legalMoves.Length; i++) {
        Move move = legalMoves[i];
        board.MakeMove(move);
        double eval = -NegaMax(board: board, depthLeft: depthLeft - 1, depthSoFar: depthSoFar + 1, 
                            color: -color, alpha: -beta, beta: -alpha, rootIsWhite: rootIsWhite, Move.NullMove, timer).Item2;
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
        int whiteScore = 0;
        int blackScore = 0;
        if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100) {
                    return -1;
                }
        if (board.IsInCheckmate()) {
            if (board.IsWhiteToMove) {
                whiteScore -= 99999999 - depthSoFar;
                }
            else {
                blackScore -= 99999999 - depthSoFar;
                }
            if (rootIsWhite) {
                return whiteScore - blackScore;
                }   
            else {
                return blackScore - whiteScore;
            }
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

        if (rootIsWhite) {
            return overallScore;
        }
        else {
            return -overallScore;
        }
    }
}
public class MyPackedEvalV1 : IChessBot
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

    const int entries = (1 << 22);
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
        double alpha = double.NegativeInfinity;
        double beta = double.PositiveInfinity;
        Move bestMove = Move.NullMove;
        double maxEval;
        Move bestMoveTemp = Move.NullMove;
        double aspiration = 50;
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE) {
            //iterative deepening
            (bestMoveTemp, maxEval) = NegaMax(board: board, depthLeft: depthLeft, 
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
    return bestMove;
    }
    public int GetNumPieces(Board board) {
        int numPieces = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        foreach (PieceList pieceList in pieces) {
            numPieces += pieceList.Count;
        }
        return numPieces;
    }


public (Move, double) NegaMax(Board board, int depthLeft, int depthSoFar, int color, double alpha, double beta, 
                                bool rootIsWhite, Move prevBestMove, Timer timer)
{
    bool root = depthSoFar == 0;
    if(!root && board.IsRepeatedPosition()) {
            return (Move.NullMove, color * -1);
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

        return (Move.NullMove, entry.score);
    }
    if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
        return (Move.NullMove, color * -77777777777);
    }
    if (depthLeft == 0 || board.IsInCheckmate() || board.IsInsufficientMaterial() || 
                board.FiftyMoveCounter >= 100)
    {
        return (Move.NullMove, color * EvaluateBoard(board, rootIsWhite, depthSoFar));
    }
    System.Span<Move> captures = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref captures, true);
    System.Span<Move> notCaptures = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref notCaptures, false);
    //remove any elements from notCaptures that are captures
    for (int i = 0; i < notCaptures.Length; i++) {
        if (notCaptures[i].IsCapture) {
            //remove this capture from notCaptures
            notCaptures[i] = notCaptures[^1];
            notCaptures = notCaptures[..^1];
            i--;
        }
    }

    //TODO: sort captures by MVV-LVA
    System.Span<int> captureScores = stackalloc int[captures.Length];
    for (int i = 0; i < captures.Length; i++) {
        captureScores[i] = (int)captures[i].MovePieceType - (int)captures[i].CapturePieceType; //1 = pawn, 2 = knight, 3 = bishop, 4 = rook, 5 = queen, 6 = king
    }
    System.MemoryExtensions.Sort(captureScores, captures);

    System.Span<Move> legalMoves = stackalloc Move[captures.Length + notCaptures.Length];
    captures.CopyTo(legalMoves);
    notCaptures.CopyTo(legalMoves[captures.Length..]);
    if (legalMoves.Length  == 0) { //stalemate detected
        return (Move.NullMove, 0);
    }
    //put the best move from the previous iteration first in the list
    if (prevBestMove != Move.NullMove) {
        for (int i = 0; i < legalMoves.Length; i++) {
            if (legalMoves[i] == prevBestMove) {
                    (legalMoves[i], legalMoves[0]) = (legalMoves[0], legalMoves[i]);
                    break;
            }
        }
    }
    double maxEval = double.NegativeInfinity;
    Move bestMove = Move.NullMove;
    double origAlpha = alpha;
    foreach (Move move in legalMoves){
        board.MakeMove(move);
        double eval = -NegaMax(board: board, depthLeft: depthLeft - 1, depthSoFar: depthSoFar + 1, 
                            color: -color, alpha: -beta, beta: -alpha, rootIsWhite: rootIsWhite, Move.NullMove, timer).Item2;
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
        int whiteScore = 0;
        int blackScore = 0;
        if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100) {
                    return -1;
                }
        if (board.IsInCheckmate()) {
            if (board.IsWhiteToMove) {
                whiteScore -= 99999999 - depthSoFar;
                }
            else {
                blackScore -= 99999999 - depthSoFar;
                }
            if (rootIsWhite) {
                return whiteScore - blackScore;
                }   
            else {
                return blackScore - whiteScore;
            }
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

        if (rootIsWhite) {
            return overallScore;
        }
        else {
            return -overallScore;
        }
    }
}


public class MyAspirationV4 : IChessBot
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

    const int entries = (1 << 22);
    TTEntry[] tt = new TTEntry[entries];

    public int positionsEvaluated = 0;
    public int TIME_PER_MOVE = 1000;
    public Move Think(Board board, Timer timer)
    {
        int depthLeft = 1;
        TIME_PER_MOVE = timer.MillisecondsRemaining / 30;
        double alpha = double.NegativeInfinity;
        double beta = double.PositiveInfinity;
        Move bestMove = Move.NullMove;
        double maxEval;
        Move bestMoveTemp = Move.NullMove;
        double aspiration = 50;
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE) {
            //iterative deepening
            (bestMoveTemp, maxEval) = NegaMax(board: board, depthLeft: depthLeft, 
                                                depthSoFar: 0, color: 1, alpha: alpha, beta: beta, 
                                                rootIsWhite: board.IsWhiteToMove, prevBestMove: bestMove, timer: timer);
            // Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}, in {4} ms\n", 
                // bestMoveTemp, maxEval, depthLeft, positionsEvaluated,timer.MillisecondsElapsedThisTurn );
            if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
                break;
            }
            //aspiration window
            if ((maxEval <= alpha || maxEval >= beta) && maxEval > -999999999 && maxEval < 999999999) { //fail low or high, ignore out of checkmate bounds and draws
                // Console.WriteLine("Search failed due to narrow aspiration window, doubling window and trying again");
                alpha -= aspiration;
                beta += aspiration;
                aspiration *= 2;
            }
            else {
                alpha = maxEval - aspiration;
                beta = maxEval + aspiration;
                // Console.WriteLine("Aspiration window: [{0}, {1}]", alpha, beta);
                depthLeft++;
                aspiration = 50;
                bestMove = bestMoveTemp;
            }
            
        }
    positionsEvaluated = 0;
    return bestMove;
    }
    public int GetNumPieces(Board board) {
        int numPieces = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        foreach (PieceList pieceList in pieces) {
            numPieces += pieceList.Count;
        }
        return numPieces;
    }


public (Move, double) NegaMax(Board board, int depthLeft, int depthSoFar, int color, double alpha, double beta, 
                                bool rootIsWhite, Move prevBestMove, Timer timer)
{
    bool root = depthSoFar == 0;
    if(!root && board.IsRepeatedPosition()) {
            return (Move.NullMove, color * -1);
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

        return (Move.NullMove, entry.score);
    }
    if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
        return (Move.NullMove, color * -77777777777);
    }
    if (depthLeft == 0 || board.IsInCheckmate() || board.IsInsufficientMaterial() || 
                board.FiftyMoveCounter >= 100)
    {
        return (Move.NullMove, color * EvaluateBoard(board, rootIsWhite, depthSoFar));
    }
    System.Span<Move> captures = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref captures, true);
    System.Span<Move> notCaptures = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref notCaptures, false);
    //remove any elements from notCaptures that are captures
    for (int i = 0; i < notCaptures.Length; i++) {
        if (notCaptures[i].IsCapture) {
            //remove this capture from notCaptures
            notCaptures[i] = notCaptures[^1];
            notCaptures = notCaptures[..^1];
            i--;
        }
    }

    //TODO: sort captures by MVV-LVA
    System.Span<int> captureScores = stackalloc int[captures.Length];
    for (int i = 0; i < captures.Length; i++) {
        captureScores[i] = (int)captures[i].MovePieceType - (int)captures[i].CapturePieceType; //1 = pawn, 2 = knight, 3 = bishop, 4 = rook, 5 = queen, 6 = king
    }
    System.MemoryExtensions.Sort(captureScores, captures);

    System.Span<Move> legalMoves = stackalloc Move[captures.Length + notCaptures.Length];
    captures.CopyTo(legalMoves);
    notCaptures.CopyTo(legalMoves[captures.Length..]);
    if (legalMoves.Length  == 0) { //stalemate detected
        return (Move.NullMove, 0);
    }
    //put the best move from the previous iteration first in the list
    if (prevBestMove != Move.NullMove) {
        for (int i = 0; i < legalMoves.Length; i++) {
            if (legalMoves[i] == prevBestMove) {
                    (legalMoves[i], legalMoves[0]) = (legalMoves[0], legalMoves[i]);
                    break;
            }
        }
    }
    double maxEval = double.NegativeInfinity;
    Move bestMove = Move.NullMove;
    double origAlpha = alpha;
    foreach (Move move in legalMoves){
        board.MakeMove(move);
        double eval = -NegaMax(board: board, depthLeft: depthLeft - 1, depthSoFar: depthSoFar + 1, 
                            color: -color, alpha: -beta, beta: -alpha, rootIsWhite: rootIsWhite, Move.NullMove, timer).Item2;
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

        public delegate int CalculateScoreDelegate(ulong pieceBitboard, bool isWhite);
    public double EvaluateBoard(Board board, bool rootIsWhite, int depthSoFar) {
        PieceType[] pieceTypes = {PieceType.Pawn, PieceType.Bishop, PieceType.Knight, PieceType.Rook, PieceType.Queen};
        positionsEvaluated++;
        double whiteScore = 0;
        double blackScore = 0;
        if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100) {
                    return -1;
                }
        if (board.IsInCheckmate()) {
            if (board.IsWhiteToMove) {
                whiteScore -= 9999999999 - depthSoFar;
                }
            else {
                blackScore -= 9999999999 - depthSoFar;
                }
            if (rootIsWhite) {
                return whiteScore - blackScore;
                }   
            else {
                return blackScore - whiteScore;
            }
                }
        ulong[] whitePieceBitBoards = new ulong[5];
        ulong[] blackPieceBitBoards = new ulong[5];
                CalculateScoreDelegate[] calculationFunctions = 
        {
            CalculatePawnScore,
            CalculateBishopScore,
            CalculateKnightScore,
            CalculateRookScore,
            CalculateQueenScore
        };
        for (int i = 0; i < 5; i++) {
            whitePieceBitBoards[i] = board.GetPieceBitboard(pieceTypes[i], white: true);
            blackPieceBitBoards[i] = board.GetPieceBitboard(pieceTypes[i], white: false);
            whiteScore += calculationFunctions[i](whitePieceBitBoards[i], isWhite: true);
            blackScore += calculationFunctions[i](blackPieceBitBoards[i], isWhite: false);
        }
        
        int numPieces = GetNumPieces(board);
        bool isEndGame = numPieces <= 12;
        whiteScore += CalculateKingScore(board.GetPieceBitboard(PieceType.King, white: true), isWhite: true, isEndGame: isEndGame);
        blackScore += CalculateKingScore(board.GetPieceBitboard(PieceType.King, white: false), isWhite: false, isEndGame: isEndGame);


        //give the color whos turn it is a slight advantage
        whiteScore += board.IsWhiteToMove ? 100 : -100;
        // //check castling
        // if (board.HasKingsideCastleRight(white: true)) {
        //     whiteScore += 20;
        // }
        // if (board.HasQueensideCastleRight(white: true)) {
        //     whiteScore += 20;
        // }
        // if (board.HasKingsideCastleRight(white: false)) {
        //     blackScore += 20;
        // }
        // if (board.HasQueensideCastleRight(white: false)) {
        //     blackScore += 20;
        // }
        if (rootIsWhite) {
            return whiteScore - blackScore;
        }
        else {
            return blackScore - whiteScore;
        }
}
public int CalculatePawnScore(ulong pawnBitboard, bool isWhite) 
{
    int score = 0;

    int[] pawnStructure = 
    {
        0,  0,  0,  0,  0,  0,  0,  0,
        50, 50, 50, 50, 50, 50, 50, 50,
        10, 10, 20, 30, 30, 20, 10, 10,
        5,  5, 10, 25, 25, 10,  5,  5,
        0,  0,  0, 20, 20,  0,  0,  0,
        5, -5,-10,  0,  0,-10, -5,  5,
        5, 10, 10,-20,-20, 10, 10,  5,
        0,  0,  0,  0,  0,  0,  0,  0
    };
    if (isWhite) 
    {
        Array.Reverse(pawnStructure);
    }

    while (pawnBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(pawnBitboard);
        score += 100 + pawnStructure[i];
        pawnBitboard &= pawnBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateKnightScore(ulong knightBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic knight structure value.
    int[] knightStructure = 
    {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50,
    };

    while (knightBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(knightBitboard);
        score += 320 + knightStructure[i];
        knightBitboard &= knightBitboard - 1; // clears least significant bit
    }

    return  score;
}

public int CalculateBishopScore(ulong bishopBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic bishop structure value.
    int[] bishopStructure = 
    {
        -20,-10,-10,-10,-10,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5, 10, 10,  5,  0,-10,
        -10,  5,  5, 10, 10,  5,  5,-10,
        -10,  0, 10, 10, 10, 10,  0,-10,
        -10, 10, 10, 10, 10, 10, 10,-10,
        -10,  5,  0,  0,  0,  0,  5,-10,
        -20,-10,-10,-10,-10,-10,-10,-20,
    };

    while (bishopBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(bishopBitboard);
        score += 330 + bishopStructure[i];
        bishopBitboard &= bishopBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateRookScore(ulong rookBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic rook structure value.
    int[] rookStructure = 
    {
        0,  0,  0,  0,  0,  0,  0,  0,
        5, 10, 10, 10, 10, 10, 10,  5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        0,  0,  0,  5,  5,  0,  0,  0
    };
    if (isWhite){ 
        Array.Reverse(rookStructure);
    }
    while (rookBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(rookBitboard);
        score += 500 + rookStructure[i];
        rookBitboard &= rookBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateQueenScore(ulong queenBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic queen structure value.
    int[] queenStructure = 
    {
        -20,-10,-10, -5, -5,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5,  5,  5,  5,  0,-10,
        -5,  0,  5,  5,  5,  5,  0, -5,
        0,  0,  5,  5,  5,  5,  0, -5,
        -10,  5,  5,  5,  5,  5,  0,-10,
        -10,  0,  5,  0,  0,  0,  0,-10,
        -20,-10,-10, -5, -5,-10,-10,-20
    };

    while (queenBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(queenBitboard);
        score += 900 + queenStructure[i];
        queenBitboard &= queenBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateKingScore(ulong kingBitboard, bool isWhite, bool isEndGame) 
{
    int score = 0;
    //we define a beginning/middle game king structure value
        int[] midKingStructure = 
    {
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -10,-20,-20,-20,-20,-20,-20,-10,
            20, 20,  0,  0,  0,  0, 20, 20,
            20, 30, 10,  0,  0, 10, 30, 20
    };
            int[] endKingStructure = 
    {
        -50,-40,-30,-20,-20,-30,-40,-50,
        -30,-20,-10,  0,  0,-10,-20,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-30,  0,  0,  0,  0,-30,-30,
        -50,-30,-30,-30,-30,-30,-30,-50
    };
    if (isWhite) {
        Array.Reverse(midKingStructure);
        Array.Reverse(endKingStructure);
    }
    int[] kingStructure = isEndGame ? endKingStructure : midKingStructure;
    while (kingBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(kingBitboard);
        score += kingStructure[i];
        kingBitboard &= kingBitboard - 1; // clears least significant bit
    }
    return score;
}


}
public class MyAspirationV3 : IChessBot
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

    const int entries = (1 << 22);
    TTEntry[] tt = new TTEntry[entries];

    public int positionsEvaluated = 0;
    public int TIME_PER_MOVE = 1000;
    public Move Think(Board board, Timer timer)
    {
        int numPieces = getNumPieces(board);
        int depthLeft = 1;
        TIME_PER_MOVE = timer.MillisecondsRemaining / 30;
        double alpha = double.NegativeInfinity;
        double beta = double.PositiveInfinity;
        Move bestMove = Move.NullMove;
        double maxEval;
        Move bestMoveTemp = Move.NullMove;
        double aspiration = 100;
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE) {
            //iterative deepening
            (bestMoveTemp, maxEval) = NegaMax(board: board, depthLeft: depthLeft, 
                                                depthSoFar: 0, color: 1, alpha: alpha, beta: beta, 
                                                rootIsWhite: board.IsWhiteToMove, prevBestMove: bestMove, timer: timer);
            // Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}, in {4} ms\n", 
            //     bestMoveTemp, maxEval, depthLeft, positionsEvaluated,timer.MillisecondsElapsedThisTurn );
            if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
                break;
            }
            //aspiration window
            if ((maxEval <= alpha || maxEval >= beta) && maxEval > -999999999 && maxEval < 999999999) { //fail low or high, ignore out of checkmate bounds and draws
                // Console.WriteLine("Search failed due to narrow aspiration window, doubling window and trying again");
                alpha = alpha - aspiration;
                beta = beta + aspiration;
                aspiration *= 2;
            }
            else {
                alpha = maxEval - aspiration;
                beta = maxEval + aspiration;
                // Console.WriteLine("Aspiration window: [{0}, {1}]", alpha, beta);
                depthLeft++;
                aspiration = 100;
                bestMove = bestMoveTemp;
            }
            
        }
    positionsEvaluated = 0;
    return bestMove;
    }
    public int getNumPieces(Board board) {
        int numPieces = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        foreach (PieceList pieceList in pieces) {
            numPieces += pieceList.Count;
        }
        return numPieces;
    }


public (Move, double) NegaMax(Board board, int depthLeft, int depthSoFar, int color, double alpha, double beta, 
                                bool rootIsWhite, Move prevBestMove, Timer timer)
{
    bool root = depthSoFar == 0;
    if(!root && board.IsRepeatedPosition()) {
            return (Move.NullMove, color * -1);
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

        return (Move.NullMove, entry.score);
    }
    if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
        return (Move.NullMove, color * -77777777777);
    }
    if (depthLeft == 0 || board.IsInCheckmate() || board.IsInsufficientMaterial() || 
                board.FiftyMoveCounter >= 100)
    {
        return (Move.NullMove, color * EvaluateBoard(board, rootIsWhite, depthSoFar));
    }
    System.Span<Move> captures = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref captures, true);
    System.Span<Move> notCaptures = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref notCaptures, false);
    //remove any elements from notCaptures that are captures
    for (int i = 0; i < notCaptures.Length; i++) {
        if (notCaptures[i].IsCapture) {
            //remove this capture from notCaptures
            notCaptures[i] = notCaptures[notCaptures.Length - 1];
            notCaptures = notCaptures.Slice(0, notCaptures.Length - 1);
            i--;
        }
    }

    //TODO: sort captures by MVV-LVA
    System.Span<int> captureScores = stackalloc int[captures.Length];
    for (int i = 0; i < captures.Length; i++) {
        captureScores[i] = (int)captures[i].MovePieceType - (int)captures[i].CapturePieceType; //1 = pawn, 2 = knight, 3 = bishop, 4 = rook, 5 = queen, 6 = king
    }
    System.MemoryExtensions.Sort(captureScores, captures);

    System.Span<Move> legalMoves = stackalloc Move[captures.Length + notCaptures.Length];
    captures.CopyTo(legalMoves);
    notCaptures.CopyTo(legalMoves.Slice(captures.Length));
    if (legalMoves.Length  == 0) { //stalemate detected
        return (Move.NullMove, 0);
    }
    //put the best move from the previous iteration first in the list
    if (prevBestMove != Move.NullMove) {
        for (int i = 0; i < legalMoves.Length; i++) {
            if (legalMoves[i] == prevBestMove) {
                Move temp = legalMoves[0];
                legalMoves[0] = legalMoves[i];
                legalMoves[i] = temp;
                break;
            }
        }
    }
    double maxEval = double.NegativeInfinity;
    Move bestMove = Move.NullMove;
    double origAlpha = alpha;
    foreach (Move move in legalMoves){
        board.MakeMove(move);
        double eval = -NegaMax(board: board, depthLeft: depthLeft - 1, depthSoFar: depthSoFar + 1, 
                            color: -color, alpha: -beta, beta: -alpha, rootIsWhite: rootIsWhite, Move.NullMove, timer).Item2;
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

        public delegate int CalculateScoreDelegate(ulong pieceBitboard, bool isWhite);
    public double EvaluateBoard(Board board, bool rootIsWhite, int depthSoFar) {
        PieceType[] pieceTypes = {PieceType.Pawn, PieceType.Bishop, PieceType.Knight, PieceType.Rook, PieceType.Queen};
        positionsEvaluated++;
        double whiteScore = 0;
        double blackScore = 0;
        if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100) {
                    return -1;
                }
        if (board.IsInCheckmate()) {
            if (board.IsWhiteToMove) {
                whiteScore -= 9999999999 - depthSoFar;
                }
            else {
                blackScore -= 9999999999 - depthSoFar;
                }
            if (rootIsWhite) {
                return whiteScore - blackScore;
                }   
            else {
                return blackScore - whiteScore;
            }
                }
        ulong[] whitePieceBitBoards = new ulong[5];
        ulong[] blackPieceBitBoards = new ulong[5];
                CalculateScoreDelegate[] calculationFunctions = 
        {
            CalculatePawnScore,
            CalculateBishopScore,
            CalculateKnightScore,
            CalculateRookScore,
            CalculateQueenScore
        };
        for (int i = 0; i < 5; i++) {
            whitePieceBitBoards[i] = board.GetPieceBitboard(pieceTypes[i], white: true);
            blackPieceBitBoards[i] = board.GetPieceBitboard(pieceTypes[i], white: false);
            whiteScore += calculationFunctions[i](whitePieceBitBoards[i], isWhite: true);
            blackScore += calculationFunctions[i](blackPieceBitBoards[i], isWhite: false);
        }
        
        int numPieces = getNumPieces(board);
        bool isEndGame = numPieces <= 12;
        whiteScore += CalculateKingScore(board.GetPieceBitboard(PieceType.King, white: true), isWhite: true, isEndGame: isEndGame);
        blackScore += CalculateKingScore(board.GetPieceBitboard(PieceType.King, white: false), isWhite: false, isEndGame: isEndGame);


        //give the color whos turn it is a slight advantage
        whiteScore += board.IsWhiteToMove ? 100 : -100;
        // //check castling
        // if (board.HasKingsideCastleRight(white: true)) {
        //     whiteScore += 20;
        // }
        // if (board.HasQueensideCastleRight(white: true)) {
        //     whiteScore += 20;
        // }
        // if (board.HasKingsideCastleRight(white: false)) {
        //     blackScore += 20;
        // }
        // if (board.HasQueensideCastleRight(white: false)) {
        //     blackScore += 20;
        // }
        if (rootIsWhite) {
            return whiteScore - blackScore;
        }
        else {
            return blackScore - whiteScore;
        }
}
public int CalculatePawnScore(ulong pawnBitboard, bool isWhite) 
{
    int score = 0;

    int[] pawnStructure = 
    {
        0,  0,  0,  0,  0,  0,  0,  0,
        50, 50, 50, 50, 50, 50, 50, 50,
        10, 10, 20, 30, 30, 20, 10, 10,
        5,  5, 10, 25, 25, 10,  5,  5,
        0,  0,  0, 20, 20,  0,  0,  0,
        5, -5,-10,  0,  0,-10, -5,  5,
        5, 10, 10,-20,-20, 10, 10,  5,
        0,  0,  0,  0,  0,  0,  0,  0
    };
    if (isWhite) 
    {
        Array.Reverse(pawnStructure);
    }

    while (pawnBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(pawnBitboard);
        score += 100 + pawnStructure[i];
        pawnBitboard &= pawnBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateKnightScore(ulong knightBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic knight structure value.
    int[] knightStructure = 
    {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50,
    };

    while (knightBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(knightBitboard);
        score += 320 + knightStructure[i];
        knightBitboard &= knightBitboard - 1; // clears least significant bit
    }

    return  score;
}

public int CalculateBishopScore(ulong bishopBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic bishop structure value.
    int[] bishopStructure = 
    {
        -20,-10,-10,-10,-10,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5, 10, 10,  5,  0,-10,
        -10,  5,  5, 10, 10,  5,  5,-10,
        -10,  0, 10, 10, 10, 10,  0,-10,
        -10, 10, 10, 10, 10, 10, 10,-10,
        -10,  5,  0,  0,  0,  0,  5,-10,
        -20,-10,-10,-10,-10,-10,-10,-20,
    };

    while (bishopBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(bishopBitboard);
        score += 330 + bishopStructure[i];
        bishopBitboard &= bishopBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateRookScore(ulong rookBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic rook structure value.
    int[] rookStructure = 
    {
        0,  0,  0,  0,  0,  0,  0,  0,
        5, 10, 10, 10, 10, 10, 10,  5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        0,  0,  0,  5,  5,  0,  0,  0
    };
    if (isWhite){ 
        Array.Reverse(rookStructure);
    }
    while (rookBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(rookBitboard);
        score += 500 + rookStructure[i];
        rookBitboard &= rookBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateQueenScore(ulong queenBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic queen structure value.
    int[] queenStructure = 
    {
        -20,-10,-10, -5, -5,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5,  5,  5,  5,  0,-10,
        -5,  0,  5,  5,  5,  5,  0, -5,
        0,  0,  5,  5,  5,  5,  0, -5,
        -10,  5,  5,  5,  5,  5,  0,-10,
        -10,  0,  5,  0,  0,  0,  0,-10,
        -20,-10,-10, -5, -5,-10,-10,-20
    };

    while (queenBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(queenBitboard);
        score += 900 + queenStructure[i];
        queenBitboard &= queenBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateKingScore(ulong kingBitboard, bool isWhite, bool isEndGame) 
{
    int score = 0;
    //we define a beginning/middle game king structure value
        int[] midKingStructure = 
    {
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -10,-20,-20,-20,-20,-20,-20,-10,
            20, 20,  0,  0,  0,  0, 20, 20,
            20, 30, 10,  0,  0, 10, 30, 20
    };
            int[] endKingStructure = 
    {
        -50,-40,-30,-20,-20,-30,-40,-50,
        -30,-20,-10,  0,  0,-10,-20,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-30,  0,  0,  0,  0,-30,-30,
        -50,-30,-30,-30,-30,-30,-30,-50
    };
    if (isWhite) {
        Array.Reverse(midKingStructure);
        Array.Reverse(endKingStructure);
    }
    int[] kingStructure = isEndGame ? endKingStructure : midKingStructure;
    while (kingBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(kingBitboard);
        score += kingStructure[i];
        kingBitboard &= kingBitboard - 1; // clears least significant bit
    }
    return score;
}


}
public class MyAspirationV2 : IChessBot
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

    const int entries = (1 << 22);
    TTEntry[] tt = new TTEntry[entries];

    public int positionsEvaluated = 0;
    public int TIME_PER_MOVE = 1000;
    public Move Think(Board board, Timer timer)
    {
        int numPieces = getNumPieces(board);
        int depthLeft = 1;
        TIME_PER_MOVE = timer.MillisecondsRemaining / 30;
        double alpha = double.NegativeInfinity;
        double beta = double.PositiveInfinity;
        Move bestMove = Move.NullMove;
        double maxEval;
        Move bestMoveTemp = Move.NullMove;
        double aspiration = 100;
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE) {
            //iterative deepening
            (bestMoveTemp, maxEval) = NegaMax(board: board, depthLeft: depthLeft, 
                                                depthSoFar: 0, color: 1, alpha: alpha, beta: beta, 
                                                rootIsWhite: board.IsWhiteToMove, prevBestMove: bestMove, timer: timer);
            // Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}, in {4} ms\n", 
            //     bestMoveTemp, maxEval, depthLeft, positionsEvaluated,timer.MillisecondsElapsedThisTurn );
            if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
                break;
            }
            //aspiration window
            if ((maxEval <= alpha || maxEval >= beta) && maxEval > -999999999 && maxEval < 999999999) { //fail low or high, ignore out of checkmate bounds and draws
                // Console.WriteLine("Search failed due to narrow aspiration window, doubling window and trying again");
                alpha = alpha - aspiration;
                beta = beta + aspiration;
                aspiration *= 2;
            }
            else {
                alpha = maxEval - aspiration;
                beta = maxEval + aspiration;
                // Console.WriteLine("Aspiration window: [{0}, {1}]", alpha, beta);
                depthLeft++;
                aspiration = 100;
                bestMove = bestMoveTemp;
            }
            
        }
    positionsEvaluated = 0;
    return bestMove;
    }
    public int getNumPieces(Board board) {
        int numPieces = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        foreach (PieceList pieceList in pieces) {
            numPieces += pieceList.Count;
        }
        return numPieces;
    }


public (Move, double) NegaMax(Board board, int depthLeft, int depthSoFar, int color, double alpha, double beta, 
                                bool rootIsWhite, Move prevBestMove, Timer timer)
{
    bool root = depthSoFar == 0;
    if(!root && board.IsRepeatedPosition()) {
            return (Move.NullMove, color * -1);
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

        return (Move.NullMove, entry.score);
    }
    if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
        return (Move.NullMove, color * -77777777777);
    }
    if (depthLeft == 0 || board.IsInCheckmate() || board.IsInsufficientMaterial() || 
                board.FiftyMoveCounter >= 100)
    {
        return (Move.NullMove, color * EvaluateBoard(board, rootIsWhite, depthSoFar));
    }
    System.Span<Move> captures = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref captures, true);
    System.Span<Move> notCaptures = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref notCaptures, false);
    //remove any elements from notCaptures that are captures
    for (int i = 0; i < notCaptures.Length; i++) {
        if (notCaptures[i].IsCapture) {
            //remove this capture from notCaptures
            notCaptures[i] = notCaptures[notCaptures.Length - 1];
            notCaptures = notCaptures.Slice(0, notCaptures.Length - 1);
            i--;
        }
    }

    // //TODO: sort captures by MVV-LVA
    // System.Span<int> captureScores = stackalloc int[captures.Length];
    // for (int i = 0; i < captures.Length; i++) {
    //     captureScores[i] = (int)captures[i].CapturePieceType - (int)captures[i].MovePieceType; //1 = pawn, 2 = knight, 3 = bishop, 4 = rook, 5 = queen
    // }
    // System.MemoryExtensions.Sort(captureScores, captures);

    System.Span<Move> legalMoves = stackalloc Move[captures.Length + notCaptures.Length];
    captures.CopyTo(legalMoves);
    notCaptures.CopyTo(legalMoves.Slice(captures.Length));
    if (legalMoves.Length  == 0) { //stalemate detected
        return (Move.NullMove, 0);
    }
    //put the best move from the previous iteration first in the list
    if (prevBestMove != Move.NullMove) {
        for (int i = 0; i < legalMoves.Length; i++) {
            if (legalMoves[i] == prevBestMove) {
                Move temp = legalMoves[0];
                legalMoves[0] = legalMoves[i];
                legalMoves[i] = temp;
                break;
            }
        }
    }
    double maxEval = double.NegativeInfinity;
    Move bestMove = Move.NullMove;
    double origAlpha = alpha;
    foreach (Move move in legalMoves){
        board.MakeMove(move);
        double eval = -NegaMax(board: board, depthLeft: depthLeft - 1, depthSoFar: depthSoFar + 1, 
                            color: -color, alpha: -beta, beta: -alpha, rootIsWhite: rootIsWhite, Move.NullMove, timer).Item2;
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

        public delegate int CalculateScoreDelegate(ulong pieceBitboard, bool isWhite);
    public double EvaluateBoard(Board board, bool rootIsWhite, int depthSoFar) {
        PieceType[] pieceTypes = {PieceType.Pawn, PieceType.Bishop, PieceType.Knight, PieceType.Rook, PieceType.Queen};
        positionsEvaluated++;
        double whiteScore = 0;
        double blackScore = 0;
        if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100) {
                    return -1;
                }
        if (board.IsInCheckmate()) {
            if (board.IsWhiteToMove) {
                whiteScore -= 9999999999 - depthSoFar;
                }
            else {
                blackScore -= 9999999999 - depthSoFar;
                }
            if (rootIsWhite) {
                return whiteScore - blackScore;
                }   
            else {
                return blackScore - whiteScore;
            }
                }
        ulong[] whitePieceBitBoards = new ulong[5];
        ulong[] blackPieceBitBoards = new ulong[5];
                CalculateScoreDelegate[] calculationFunctions = 
        {
            CalculatePawnScore,
            CalculateBishopScore,
            CalculateKnightScore,
            CalculateRookScore,
            CalculateQueenScore
        };
        for (int i = 0; i < 5; i++) {
            whitePieceBitBoards[i] = board.GetPieceBitboard(pieceTypes[i], white: true);
            blackPieceBitBoards[i] = board.GetPieceBitboard(pieceTypes[i], white: false);
            whiteScore += calculationFunctions[i](whitePieceBitBoards[i], isWhite: true);
            blackScore += calculationFunctions[i](blackPieceBitBoards[i], isWhite: false);
        }
        
        int numPieces = getNumPieces(board);
        bool isEndGame = numPieces <= 12;
        whiteScore += CalculateKingScore(board.GetPieceBitboard(PieceType.King, white: true), isWhite: true, isEndGame: isEndGame);
        blackScore += CalculateKingScore(board.GetPieceBitboard(PieceType.King, white: false), isWhite: false, isEndGame: isEndGame);


        //give the color whos turn it is a slight advantage
        whiteScore += board.IsWhiteToMove ? 100 : -100;
        // //check castling
        // if (board.HasKingsideCastleRight(white: true)) {
        //     whiteScore += 20;
        // }
        // if (board.HasQueensideCastleRight(white: true)) {
        //     whiteScore += 20;
        // }
        // if (board.HasKingsideCastleRight(white: false)) {
        //     blackScore += 20;
        // }
        // if (board.HasQueensideCastleRight(white: false)) {
        //     blackScore += 20;
        // }
        if (rootIsWhite) {
            return whiteScore - blackScore;
        }
        else {
            return blackScore - whiteScore;
        }
}
public int CalculatePawnScore(ulong pawnBitboard, bool isWhite) 
{
    int score = 0;

    int[] pawnStructure = 
    {
        0,  0,  0,  0,  0,  0,  0,  0,
        50, 50, 50, 50, 50, 50, 50, 50,
        10, 10, 20, 30, 30, 20, 10, 10,
        5,  5, 10, 25, 25, 10,  5,  5,
        0,  0,  0, 20, 20,  0,  0,  0,
        5, -5,-10,  0,  0,-10, -5,  5,
        5, 10, 10,-20,-20, 10, 10,  5,
        0,  0,  0,  0,  0,  0,  0,  0
    };
    if (isWhite) 
    {
        Array.Reverse(pawnStructure);
    }

    while (pawnBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(pawnBitboard);
        score += 100 + pawnStructure[i];
        pawnBitboard &= pawnBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateKnightScore(ulong knightBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic knight structure value.
    int[] knightStructure = 
    {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50,
    };

    while (knightBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(knightBitboard);
        score += 320 + knightStructure[i];
        knightBitboard &= knightBitboard - 1; // clears least significant bit
    }

    return  score;
}

public int CalculateBishopScore(ulong bishopBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic bishop structure value.
    int[] bishopStructure = 
    {
        -20,-10,-10,-10,-10,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5, 10, 10,  5,  0,-10,
        -10,  5,  5, 10, 10,  5,  5,-10,
        -10,  0, 10, 10, 10, 10,  0,-10,
        -10, 10, 10, 10, 10, 10, 10,-10,
        -10,  5,  0,  0,  0,  0,  5,-10,
        -20,-10,-10,-10,-10,-10,-10,-20,
    };

    while (bishopBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(bishopBitboard);
        score += 330 + bishopStructure[i];
        bishopBitboard &= bishopBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateRookScore(ulong rookBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic rook structure value.
    int[] rookStructure = 
    {
        0,  0,  0,  0,  0,  0,  0,  0,
        5, 10, 10, 10, 10, 10, 10,  5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        0,  0,  0,  5,  5,  0,  0,  0
    };
    if (isWhite){ 
        Array.Reverse(rookStructure);
    }
    while (rookBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(rookBitboard);
        score += 500 + rookStructure[i];
        rookBitboard &= rookBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateQueenScore(ulong queenBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic queen structure value.
    int[] queenStructure = 
    {
        -20,-10,-10, -5, -5,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5,  5,  5,  5,  0,-10,
        -5,  0,  5,  5,  5,  5,  0, -5,
        0,  0,  5,  5,  5,  5,  0, -5,
        -10,  5,  5,  5,  5,  5,  0,-10,
        -10,  0,  5,  0,  0,  0,  0,-10,
        -20,-10,-10, -5, -5,-10,-10,-20
    };

    while (queenBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(queenBitboard);
        score += 900 + queenStructure[i];
        queenBitboard &= queenBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateKingScore(ulong kingBitboard, bool isWhite, bool isEndGame) 
{
    int score = 0;
    //we define a beginning/middle game king structure value
        int[] midKingStructure = 
    {
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -10,-20,-20,-20,-20,-20,-20,-10,
            20, 20,  0,  0,  0,  0, 20, 20,
            20, 30, 10,  0,  0, 10, 30, 20
    };
            int[] endKingStructure = 
    {
        -50,-40,-30,-20,-20,-30,-40,-50,
        -30,-20,-10,  0,  0,-10,-20,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-30,  0,  0,  0,  0,-30,-30,
        -50,-30,-30,-30,-30,-30,-30,-50
    };
    if (isWhite) {
        Array.Reverse(midKingStructure);
        Array.Reverse(endKingStructure);
    }
    int[] kingStructure = isEndGame ? endKingStructure : midKingStructure;
    while (kingBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(kingBitboard);
        score += kingStructure[i];
        kingBitboard &= kingBitboard - 1; // clears least significant bit
    }
    return score;
}


}



    public class MyAspirationV1 : IChessBot
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

    const int entries = (1 << 22);
    TTEntry[] tt = new TTEntry[entries];

    public int positionsEvaluated = 0;
    public int TIME_PER_MOVE = 1000;
    public Move Think(Board board, Timer timer)
    {
        int numPieces = getNumPieces(board);
        int depthLeft = 2;
        TIME_PER_MOVE = timer.MillisecondsRemaining / 30;
        double alpha = double.NegativeInfinity;
        double beta = double.PositiveInfinity;
        Move bestMove = Move.NullMove;
        double maxEval;
        double aspiration = 100; //1 pawn window
        Move bestMoveTemp = Move.NullMove;

        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE) {
            //iterative deepening
            (bestMoveTemp, maxEval) = NegaMax(board: board, depthLeft: depthLeft, 
                                                depthSoFar: 0, color: 1, alpha: alpha, beta: beta, 
                                                rootIsWhite: board.IsWhiteToMove, prevBestMove: bestMove, timer: timer);
            //Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}, in {4} ms\n", 
                //bestMoveTemp, maxEval, depthLeft, positionsEvaluated,timer.MillisecondsElapsedThisTurn );
            if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
                break;
            }
            //aspiration window
            if ((maxEval <= alpha || maxEval >= beta) && maxEval > -999999999 && maxEval < 999999999) { //fail low or high, ignore out of checkmate bounds and draws
                //Console.WriteLine("Search failed due to narrow aspiration window, doubling window and trying again");
                alpha = alpha - aspiration;
                beta = beta + aspiration;
                aspiration *= 2;
            }
            else {
                alpha = maxEval - aspiration;
                beta = maxEval + aspiration;
                //Console.WriteLine("Aspiration window: [{0}, {1}]", alpha, beta);
                depthLeft += 1;
                aspiration = 100;
                bestMove = bestMoveTemp;
            }
            
        }
    positionsEvaluated = 0;
    return bestMove;
    }
    public int getNumPieces(Board board) {
        int numPieces = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        foreach (PieceList pieceList in pieces) {
            numPieces += pieceList.Count;
        }
        return numPieces;
    }


public (Move, double) NegaMax(Board board, int depthLeft, int depthSoFar, int color, double alpha, double beta, 
                                bool rootIsWhite, Move prevBestMove, Timer timer)
{
    bool root = depthSoFar == 0;
    if(!root && board.IsRepeatedPosition()) {
            return (Move.NullMove, color * -1);
    }

    ulong key = board.ZobristKey;
    TTEntry entry = tt[key % entries];

    if(!root && entry.key == key && entry.depth >= depthLeft && (
            entry.bound == 3 // exact score
                || entry.bound == 2 && entry.score >= beta // lower bound, fail high
                || entry.bound == 1 && entry.score <= alpha // upper bound, fail low
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

        return (Move.NullMove, entry.score);
    }
    if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
        return (Move.NullMove, color * -77777777777);
    }
    if (depthLeft == 0 || board.IsInCheckmate() || board.IsInsufficientMaterial() || 
                board.FiftyMoveCounter >= 100)
    {
        return (Move.NullMove, color * EvaluateBoard(board, rootIsWhite, depthSoFar));
    }
    System.Span<Move> captures = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref captures, true);
    System.Span<Move> notCaptures = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref notCaptures, false);
    //remove any elements from notCaptures that are in captures
    for (int i = 0; i < captures.Length; i++) {
        for (int j = 0; j < notCaptures.Length; j++) {
            if (captures[i] == notCaptures[j]) {
                notCaptures[j] = notCaptures[notCaptures.Length - 1];
                notCaptures = notCaptures.Slice(0, notCaptures.Length - 1);
                break;
            }
        }
    }

    System.Span<Move> legalMoves = stackalloc Move[captures.Length + notCaptures.Length];
    captures.CopyTo(legalMoves);
    notCaptures.CopyTo(legalMoves.Slice(captures.Length));
    if (legalMoves.Length  == 0) { //stalemate detected
        return (Move.NullMove, 0);
    }
    //put the best move from the previous iteration first in the list
    if (prevBestMove != Move.NullMove) {
        for (int i = 0; i < legalMoves.Length; i++) {
            if (legalMoves[i] == prevBestMove) {
                Move temp = legalMoves[0];
                legalMoves[0] = legalMoves[i];
                legalMoves[i] = temp;
                break;
            }
        }
    }
    double maxEval = double.NegativeInfinity;
    Move bestMove = Move.NullMove;
    double origAlpha = alpha;
    foreach (Move move in legalMoves){
        board.MakeMove(move);
        double eval = -NegaMax(board: board, depthLeft: depthLeft - 1, depthSoFar: depthSoFar + 1, 
                            color: -color, alpha: -beta, beta: -alpha, rootIsWhite: rootIsWhite, Move.NullMove, timer).Item2;
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

        public delegate int CalculateScoreDelegate(ulong pieceBitboard, bool isWhite);
    public double EvaluateBoard(Board board, bool rootIsWhite, int depthSoFar) {
        PieceType[] pieceTypes = {PieceType.Pawn, PieceType.Bishop, PieceType.Knight, PieceType.Rook, PieceType.Queen};
        positionsEvaluated++;
        double whiteScore = 0;
        double blackScore = 0;
        if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100) {
                    return -1;
                }
        if (board.IsInCheckmate()) {
            if (board.IsWhiteToMove) {
                whiteScore -= 9999999999 - depthSoFar;
                }
            else {
                blackScore -= 9999999999 - depthSoFar;
                }
            if (rootIsWhite) {
                return whiteScore - blackScore;
                }   
            else {
                return blackScore - whiteScore;
            }
                }
        ulong[] whitePieceBitBoards = new ulong[5];
        ulong[] blackPieceBitBoards = new ulong[5];
                CalculateScoreDelegate[] calculationFunctions = 
        {
            CalculatePawnScore,
            CalculateBishopScore,
            CalculateKnightScore,
            CalculateRookScore,
            CalculateQueenScore
        };
        for (int i = 0; i < 5; i++) {
            whitePieceBitBoards[i] = board.GetPieceBitboard(pieceTypes[i], white: true);
            blackPieceBitBoards[i] = board.GetPieceBitboard(pieceTypes[i], white: false);
            whiteScore += calculationFunctions[i](whitePieceBitBoards[i], isWhite: true);
            blackScore += calculationFunctions[i](blackPieceBitBoards[i], isWhite: false);
        }
        
        int numPieces = getNumPieces(board);
        bool isEndGame = numPieces <= 12;
        whiteScore += CalculateKingScore(board.GetPieceBitboard(PieceType.King, white: true), isWhite: true, isEndGame: isEndGame);
        blackScore += CalculateKingScore(board.GetPieceBitboard(PieceType.King, white: false), isWhite: false, isEndGame: isEndGame);


        //give the color whos turn it is a slight advantage
        whiteScore += board.IsWhiteToMove ? 100 : -100;
        // //check castling
        // if (board.HasKingsideCastleRight(white: true)) {
        //     whiteScore += 20;
        // }
        // if (board.HasQueensideCastleRight(white: true)) {
        //     whiteScore += 20;
        // }
        // if (board.HasKingsideCastleRight(white: false)) {
        //     blackScore += 20;
        // }
        // if (board.HasQueensideCastleRight(white: false)) {
        //     blackScore += 20;
        // }
        if (rootIsWhite) {
            return whiteScore - blackScore;
        }
        else {
            return blackScore - whiteScore;
        }
}
public int CalculatePawnScore(ulong pawnBitboard, bool isWhite) 
{
    int score = 0;

    int[] pawnStructure = 
    {
        0,  0,  0,  0,  0,  0,  0,  0,
        50, 50, 50, 50, 50, 50, 50, 50,
        10, 10, 20, 30, 30, 20, 10, 10,
        5,  5, 10, 25, 25, 10,  5,  5,
        0,  0,  0, 20, 20,  0,  0,  0,
        5, -5,-10,  0,  0,-10, -5,  5,
        5, 10, 10,-20,-20, 10, 10,  5,
        0,  0,  0,  0,  0,  0,  0,  0
    };
    if (isWhite) 
    {
        Array.Reverse(pawnStructure);
    }

    while (pawnBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(pawnBitboard);
        score += 100 + pawnStructure[i];
        pawnBitboard &= pawnBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateKnightScore(ulong knightBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic knight structure value.
    int[] knightStructure = 
    {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50,
    };

    while (knightBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(knightBitboard);
        score += 320 + knightStructure[i];
        knightBitboard &= knightBitboard - 1; // clears least significant bit
    }

    return  score;
}

public int CalculateBishopScore(ulong bishopBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic bishop structure value.
    int[] bishopStructure = 
    {
        -20,-10,-10,-10,-10,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5, 10, 10,  5,  0,-10,
        -10,  5,  5, 10, 10,  5,  5,-10,
        -10,  0, 10, 10, 10, 10,  0,-10,
        -10, 10, 10, 10, 10, 10, 10,-10,
        -10,  5,  0,  0,  0,  0,  5,-10,
        -20,-10,-10,-10,-10,-10,-10,-20,
    };

    while (bishopBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(bishopBitboard);
        score += 330 + bishopStructure[i];
        bishopBitboard &= bishopBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateRookScore(ulong rookBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic rook structure value.
    int[] rookStructure = 
    {
        0,  0,  0,  0,  0,  0,  0,  0,
        5, 10, 10, 10, 10, 10, 10,  5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        0,  0,  0,  5,  5,  0,  0,  0
    };
    if (isWhite){ 
        Array.Reverse(rookStructure);
    }
    while (rookBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(rookBitboard);
        score += 500 + rookStructure[i];
        rookBitboard &= rookBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateQueenScore(ulong queenBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic queen structure value.
    int[] queenStructure = 
    {
        -20,-10,-10, -5, -5,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5,  5,  5,  5,  0,-10,
        -5,  0,  5,  5,  5,  5,  0, -5,
        0,  0,  5,  5,  5,  5,  0, -5,
        -10,  5,  5,  5,  5,  5,  0,-10,
        -10,  0,  5,  0,  0,  0,  0,-10,
        -20,-10,-10, -5, -5,-10,-10,-20
    };

    while (queenBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(queenBitboard);
        score += 900 + queenStructure[i];
        queenBitboard &= queenBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateKingScore(ulong kingBitboard, bool isWhite, bool isEndGame) 
{
    int score = 0;
    //we define a beginning/middle game king structure value
        int[] midKingStructure = 
    {
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -10,-20,-20,-20,-20,-20,-20,-10,
            20, 20,  0,  0,  0,  0, 20, 20,
            20, 30, 10,  0,  0, 10, 30, 20
    };
            int[] endKingStructure = 
    {
        -50,-40,-30,-20,-20,-30,-40,-50,
        -30,-20,-10,  0,  0,-10,-20,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-30,  0,  0,  0,  0,-30,-30,
        -50,-30,-30,-30,-30,-30,-30,-50
    };
    if (isWhite) {
        Array.Reverse(midKingStructure);
        Array.Reverse(endKingStructure);
    }
    int[] kingStructure = isEndGame ? endKingStructure : midKingStructure;
    while (kingBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(kingBitboard);
        score += kingStructure[i];
        kingBitboard &= kingBitboard - 1; // clears least significant bit
    }
    return score;
}


}
public class MyTTV1 : IChessBot
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

    const int entries = (1 << 22);
    TTEntry[] tt = new TTEntry[entries];

    public int positionsEvaluated = 0;
    public int TIME_PER_MOVE = 1000;
    public Move Think(Board board, Timer timer)
    {
        // Move[] legalMoves = getMovesSorted(board);
        int numPieces = getNumPieces(board);
        int depthLeft = 2;
        Move bestMoveFinal = Move.NullMove;
        double bestEvalFinal = double.NegativeInfinity;
        Move bestMoveCurrent = Move.NullMove;
        double bestEvalCurrent = double.NegativeInfinity;
        // TIME_PER_MOVE = timer.MillisecondsRemaining / numPieces;
        // if (timer.MillisecondsRemaining < 6000) {
        TIME_PER_MOVE = timer.MillisecondsRemaining / 30;
        // }
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE) {
            //iterative deepening
            double alpha = double.NegativeInfinity;
            double beta = double.PositiveInfinity;
            (bestMoveCurrent, bestEvalCurrent) = NegaMax(board: board, depthLeft: depthLeft, 
                                                depthSoFar: 0, color: 1, alpha: alpha, beta: beta, 
                                                rootIsWhite: board.IsWhiteToMove, prevBestMove: bestMoveCurrent, timer: timer);
            if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
                break;
            }
            bestMoveFinal = bestMoveCurrent;
            bestEvalFinal = bestEvalCurrent;
            //Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}, in {4} ms\n", 
                            //bestMoveFinal, bestEvalFinal, depthLeft, positionsEvaluated,timer.MillisecondsElapsedThisTurn );
            depthLeft += 1;
            
        }
    positionsEvaluated = 0;
    return bestMoveFinal;
    }
    public int getNumPieces(Board board) {
        int numPieces = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        foreach (PieceList pieceList in pieces) {
            numPieces += pieceList.Count;
        }
        return numPieces;
    }


public (Move, double) NegaMax(Board board, int depthLeft, int depthSoFar, int color, double alpha, double beta, 
                                bool rootIsWhite, Move prevBestMove, Timer timer)
{
    bool root = depthSoFar == 0;
    if(!root && board.IsRepeatedPosition()) {
            return (Move.NullMove, 0);
    }

    ulong key = board.ZobristKey;
    TTEntry entry = tt[key % entries];

    if(!root && entry.key == key && entry.depth >= depthLeft && (
            entry.bound == 3 // exact score
                || entry.bound == 2 && entry.score >= beta // lower bound, fail high
                || entry.bound == 1 && entry.score <= alpha // upper bound, fail low
        )) {
        return (Move.NullMove, entry.score);
    }

    if (depthLeft == 0 || board.IsInCheckmate() || board.IsInsufficientMaterial() || 
                board.FiftyMoveCounter >= 100 || timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE)
    {
        return (Move.NullMove, color * EvaluateBoard(board, rootIsWhite, depthSoFar));
    }
    System.Span<Move> captures = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref captures, true);
    System.Span<Move> notCaptures = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref notCaptures, false);
    //remove any elements from notCaptures that are in captures
    for (int i = 0; i < captures.Length; i++) {
        for (int j = 0; j < notCaptures.Length; j++) {
            if (captures[i] == notCaptures[j]) {
                notCaptures[j] = notCaptures[notCaptures.Length - 1];
                notCaptures = notCaptures.Slice(0, notCaptures.Length - 1);
                break;
            }
        }
    }

    System.Span<Move> legalMoves = stackalloc Move[captures.Length + notCaptures.Length];
    captures.CopyTo(legalMoves);
    notCaptures.CopyTo(legalMoves.Slice(captures.Length));
    if (legalMoves.Length  == 0) { //stalemate detected
        return (Move.NullMove, 0);
    }
    //put the best move from the previous iteration first in the list
    if (prevBestMove != Move.NullMove) {
        for (int i = 0; i < legalMoves.Length; i++) {
            if (legalMoves[i] == prevBestMove) {
                Move temp = legalMoves[0];
                legalMoves[0] = legalMoves[i];
                legalMoves[i] = temp;
                break;
            }
        }
    }
    double maxEval = double.NegativeInfinity;
    Move bestMove = Move.NullMove;
    double origAlpha = alpha;
    foreach (Move move in legalMoves)
    {
        board.MakeMove(move);
        double eval = -NegaMax(board: board, depthLeft: depthLeft - 1, depthSoFar: depthSoFar + 1, 
                            color: -color, alpha: -beta, beta: -alpha, rootIsWhite: rootIsWhite, Move.NullMove, timer).Item2;
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
    }   int bound = maxEval >= beta ? 2 : maxEval > origAlpha ? 3 : 1;

        // Push to TT
        tt[key % entries] = new TTEntry(key, bestMove, depthLeft, maxEval, bound);
    
    return (bestMove, maxEval);
}

        public delegate int CalculateScoreDelegate(ulong pieceBitboard, bool isWhite);
    public double EvaluateBoard(Board board, bool rootIsWhite, int depthSoFar) {
        PieceType[] pieceTypes = {PieceType.Pawn, PieceType.Bishop, PieceType.Knight, PieceType.Rook, PieceType.Queen};
        positionsEvaluated++;
        double whiteScore = 0;
        double blackScore = 0;
        if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100) {
                    return -1;
                }
        if (board.IsInCheckmate()) {
            if (board.IsWhiteToMove) {
                whiteScore -= 9999999999 - depthSoFar;
                }
            else {
                blackScore -= 9999999999 - depthSoFar;
                }
            if (rootIsWhite) {
                return whiteScore - blackScore;
                }   
            else {
                return blackScore - whiteScore;
            }
                }
        ulong[] whitePieceBitBoards = new ulong[5];
        ulong[] blackPieceBitBoards = new ulong[5];
                CalculateScoreDelegate[] calculationFunctions = 
        {
            CalculatePawnScore,
            CalculateBishopScore,
            CalculateKnightScore,
            CalculateRookScore,
            CalculateQueenScore
        };
        for (int i = 0; i < 5; i++) {
            whitePieceBitBoards[i] = board.GetPieceBitboard(pieceTypes[i], white: true);
            blackPieceBitBoards[i] = board.GetPieceBitboard(pieceTypes[i], white: false);
            whiteScore += calculationFunctions[i](whitePieceBitBoards[i], isWhite: true);
            blackScore += calculationFunctions[i](blackPieceBitBoards[i], isWhite: false);
        }
        
        int numPieces = getNumPieces(board);
        bool isEndGame = numPieces <= 12;
        whiteScore += CalculateKingScore(board.GetPieceBitboard(PieceType.King, white: true), isWhite: true, isEndGame: isEndGame);
        blackScore += CalculateKingScore(board.GetPieceBitboard(PieceType.King, white: false), isWhite: false, isEndGame: isEndGame);


        //give the color whos turn it is a slight advantage
        whiteScore += board.IsWhiteToMove ? 100 : -100;
        // //check castling
        // if (board.HasKingsideCastleRight(white: true)) {
        //     whiteScore += 20;
        // }
        // if (board.HasQueensideCastleRight(white: true)) {
        //     whiteScore += 20;
        // }
        // if (board.HasKingsideCastleRight(white: false)) {
        //     blackScore += 20;
        // }
        // if (board.HasQueensideCastleRight(white: false)) {
        //     blackScore += 20;
        // }
        if (rootIsWhite) {
            return whiteScore - blackScore;
        }
        else {
            return blackScore - whiteScore;
        }
}
public int CalculatePawnScore(ulong pawnBitboard, bool isWhite) 
{
    int score = 0;

    int[] pawnStructure = 
    {
        0,  0,  0,  0,  0,  0,  0,  0,
        50, 50, 50, 50, 50, 50, 50, 50,
        10, 10, 20, 30, 30, 20, 10, 10,
        5,  5, 10, 25, 25, 10,  5,  5,
        0,  0,  0, 20, 20,  0,  0,  0,
        5, -5,-10,  0,  0,-10, -5,  5,
        5, 10, 10,-20,-20, 10, 10,  5,
        0,  0,  0,  0,  0,  0,  0,  0
    };
    if (isWhite) 
    {
        Array.Reverse(pawnStructure);
    }

    while (pawnBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(pawnBitboard);
        score += 100 + pawnStructure[i];
        pawnBitboard &= pawnBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateKnightScore(ulong knightBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic knight structure value.
    int[] knightStructure = 
    {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50,
    };

    while (knightBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(knightBitboard);
        score += 320 + knightStructure[i];
        knightBitboard &= knightBitboard - 1; // clears least significant bit
    }

    return  score;
}

public int CalculateBishopScore(ulong bishopBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic bishop structure value.
    int[] bishopStructure = 
    {
        -20,-10,-10,-10,-10,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5, 10, 10,  5,  0,-10,
        -10,  5,  5, 10, 10,  5,  5,-10,
        -10,  0, 10, 10, 10, 10,  0,-10,
        -10, 10, 10, 10, 10, 10, 10,-10,
        -10,  5,  0,  0,  0,  0,  5,-10,
        -20,-10,-10,-10,-10,-10,-10,-20,
    };

    while (bishopBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(bishopBitboard);
        score += 330 + bishopStructure[i];
        bishopBitboard &= bishopBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateRookScore(ulong rookBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic rook structure value.
    int[] rookStructure = 
    {
        0,  0,  0,  0,  0,  0,  0,  0,
        5, 10, 10, 10, 10, 10, 10,  5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        0,  0,  0,  5,  5,  0,  0,  0
    };
    if (isWhite){ 
        Array.Reverse(rookStructure);
    }
    while (rookBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(rookBitboard);
        score += 500 + rookStructure[i];
        rookBitboard &= rookBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateQueenScore(ulong queenBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic queen structure value.
    int[] queenStructure = 
    {
        -20,-10,-10, -5, -5,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5,  5,  5,  5,  0,-10,
        -5,  0,  5,  5,  5,  5,  0, -5,
        0,  0,  5,  5,  5,  5,  0, -5,
        -10,  5,  5,  5,  5,  5,  0,-10,
        -10,  0,  5,  0,  0,  0,  0,-10,
        -20,-10,-10, -5, -5,-10,-10,-20
    };

    while (queenBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(queenBitboard);
        score += 900 + queenStructure[i];
        queenBitboard &= queenBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateKingScore(ulong kingBitboard, bool isWhite, bool isEndGame) 
{
    int score = 0;
    //we define a beginning/middle game king structure value
        int[] midKingStructure = 
    {
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -10,-20,-20,-20,-20,-20,-20,-10,
            20, 20,  0,  0,  0,  0, 20, 20,
            20, 30, 10,  0,  0, 10, 30, 20
    };
            int[] endKingStructure = 
    {
        -50,-40,-30,-20,-20,-30,-40,-50,
        -30,-20,-10,  0,  0,-10,-20,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-30,  0,  0,  0,  0,-30,-30,
        -50,-30,-30,-30,-30,-30,-30,-50
    };
    if (isWhite) {
        Array.Reverse(midKingStructure);
        Array.Reverse(endKingStructure);
    }
    int[] kingStructure = isEndGame ? endKingStructure : midKingStructure;
    while (kingBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(kingBitboard);
        score += kingStructure[i];
        kingBitboard &= kingBitboard - 1; // clears least significant bit
    }
    return score;
}


}
public class MyIterDeepPSEV2 : IChessBot
{
    public int positionsEvaluated = 0;
    public int TIME_PER_MOVE =  1000;
    public Move Think(Board board, Timer timer)
    {
        // Move[] legalMoves = getMovesSorted(board);
        int numPieces = getNumPieces(board);
        int maxDepth = 10;
        int depthLeft = 1;
        Move bestMoveFinal = Move.NullMove;
        double bestEvalFinal = double.NegativeInfinity;
        Move bestMoveCurrent = Move.NullMove;
        double bestEvalCurrent = double.NegativeInfinity;
        if (timer.MillisecondsRemaining < 10000) {
            maxDepth = 7;
        }
        if (timer.MillisecondsRemaining < 6000) {
            maxDepth = 5;
        }
        if (timer.MillisecondsRemaining < 3000) {
            maxDepth = 4;
        }
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE && depthLeft < maxDepth) {
            //iterative deepening
            double alpha = double.NegativeInfinity;
            double beta = double.PositiveInfinity;
            (bestMoveCurrent, bestEvalCurrent) = NegaMax(board: board, depthLeft: depthLeft, 
                                                depthSoFar: 0, color: 1, alpha: alpha, beta: beta, 
                                                rootIsWhite: board.IsWhiteToMove, prevBestMove: bestMoveCurrent, timer: timer);
            if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
                break;
            }
            bestMoveFinal = bestMoveCurrent;
            bestEvalFinal = bestEvalCurrent;
            //Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}\n", bestMoveFinal, bestEvalFinal, depthLeft, positionsEvaluated);
            depthLeft++;
            
        }
    positionsEvaluated = 0;
    return bestMoveFinal;
    }
    public int getNumPieces(Board board) {
        int numPieces = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        foreach (PieceList pieceList in pieces) {
            numPieces += pieceList.Count;
        }
        return numPieces;
    }


public (Move, double) NegaMax(Board board, int depthLeft, int depthSoFar, int color, double alpha, double beta, 
                                bool rootIsWhite, Move prevBestMove, Timer timer)
{
    if (depthLeft == 0 || board.IsInCheckmate() || board.IsInsufficientMaterial() || board.IsRepeatedPosition() || 
                board.FiftyMoveCounter >= 100 || timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE)
    {
        return (Move.NullMove, color * EvaluateBoard(board, rootIsWhite, depthSoFar));
    }
    //put the best move from the previous iteration first in the list
    System.Span<Move> captures = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref captures, true);
    System.Span<Move> notCaptures = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref notCaptures, false);
    //remove any elements from notCaptures that are in captures
    for (int i = 0; i < captures.Length; i++) {
        for (int j = 0; j < notCaptures.Length; j++) {
            if (captures[i] == notCaptures[j]) {
                notCaptures[j] = notCaptures[notCaptures.Length - 1];
                notCaptures = notCaptures.Slice(0, notCaptures.Length - 1);
                break;
            }
        }
    }
    System.Span<Move> legalMoves = stackalloc Move[captures.Length + notCaptures.Length];
    captures.CopyTo(legalMoves);
    notCaptures.CopyTo(legalMoves.Slice(captures.Length));
    if (legalMoves.Length  == 0) { //stalemate detected
        return (Move.NullMove, 0);
    }
    if (prevBestMove != Move.NullMove) {
        for (int i = 0; i < legalMoves.Length; i++) {
            if (legalMoves[i] == prevBestMove) {
                Move temp = legalMoves[0];
                legalMoves[0] = legalMoves[i];
                legalMoves[i] = temp;
                break;
            }
        }
    }
    double maxEval = double.NegativeInfinity;
    Move bestMove = Move.NullMove;

    foreach (Move move in legalMoves)
    {
        board.MakeMove(move);
        double eval = -NegaMax(board: board, depthLeft: depthLeft - 1, depthSoFar: depthSoFar + 1, 
                            color: -color, alpha: -beta, beta: -alpha, rootIsWhite: rootIsWhite, Move.NullMove, timer).Item2;
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
    
    return (bestMove, maxEval);
}

        public delegate int CalculateScoreDelegate(ulong pieceBitboard, bool isWhite);
    public double EvaluateBoard(Board board, bool rootIsWhite, int depthSoFar) {
        PieceType[] pieceTypes = {PieceType.Pawn, PieceType.Bishop, PieceType.Knight, PieceType.Rook, PieceType.Queen};
        positionsEvaluated++;
        double whiteScore = 0;
        double blackScore = 0;
        if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100) {
                    return -1;
                }
        if (board.IsInCheckmate()) {
            if (board.IsWhiteToMove) {
                whiteScore -= 9999999999 - depthSoFar;
                }
            else {
                blackScore -= 9999999999 - depthSoFar;
                }
            if (rootIsWhite) {
                return whiteScore - blackScore;
                }   
            else {
                return blackScore - whiteScore;
            }
                }
        ulong[] whitePieceBitBoards = new ulong[5];
        ulong[] blackPieceBitBoards = new ulong[5];
                CalculateScoreDelegate[] calculationFunctions = 
        {
            CalculatePawnScore,
            CalculateBishopScore,
            CalculateKnightScore,
            CalculateRookScore,
            CalculateQueenScore
        };
        for (int i = 0; i < 5; i++) {
            whitePieceBitBoards[i] = board.GetPieceBitboard(pieceTypes[i], white: true);
            blackPieceBitBoards[i] = board.GetPieceBitboard(pieceTypes[i], white: false);
            whiteScore += calculationFunctions[i](whitePieceBitBoards[i], isWhite: true);
            blackScore += calculationFunctions[i](blackPieceBitBoards[i], isWhite: false);
        }
        
        int numPieces = getNumPieces(board);
        bool isEndGame = numPieces <= 12;
        whiteScore += CalculateKingScore(board.GetPieceBitboard(PieceType.King, white: true), isWhite: true, isEndGame: isEndGame);
        blackScore += CalculateKingScore(board.GetPieceBitboard(PieceType.King, white: false), isWhite: false, isEndGame: isEndGame);

        // //check castling
        // if (board.HasKingsideCastleRight(white: true)) {
        //     whiteScore += 20;
        // }
        // if (board.HasQueensideCastleRight(white: true)) {
        //     whiteScore += 20;
        // }
        // if (board.HasKingsideCastleRight(white: false)) {
        //     blackScore += 20;
        // }
        // if (board.HasQueensideCastleRight(white: false)) {
        //     blackScore += 20;
        // }
        if (rootIsWhite) {
            return whiteScore - blackScore;
        }
        else {
            return blackScore - whiteScore;
        }
}
public int CalculatePawnScore(ulong pawnBitboard, bool isWhite) 
{
    int score = 0;

    int[] pawnStructure = 
    {
        0,  0,  0,  0,  0,  0,  0,  0,
        50, 50, 50, 50, 50, 50, 50, 50,
        10, 10, 20, 30, 30, 20, 10, 10,
        5,  5, 10, 25, 25, 10,  5,  5,
        0,  0,  0, 20, 20,  0,  0,  0,
        5, -5,-10,  0,  0,-10, -5,  5,
        5, 10, 10,-20,-20, 10, 10,  5,
        0,  0,  0,  0,  0,  0,  0,  0
    };
    if (isWhite) 
    {
        Array.Reverse(pawnStructure);
    }

    while (pawnBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(pawnBitboard);
        score += 100 + pawnStructure[i];
        pawnBitboard &= pawnBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateKnightScore(ulong knightBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic knight structure value.
    int[] knightStructure = 
    {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50,
    };

    while (knightBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(knightBitboard);
        score += 320 + knightStructure[i];
        knightBitboard &= knightBitboard - 1; // clears least significant bit
    }

    return  score;
}

public int CalculateBishopScore(ulong bishopBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic bishop structure value.
    int[] bishopStructure = 
    {
        -20,-10,-10,-10,-10,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5, 10, 10,  5,  0,-10,
        -10,  5,  5, 10, 10,  5,  5,-10,
        -10,  0, 10, 10, 10, 10,  0,-10,
        -10, 10, 10, 10, 10, 10, 10,-10,
        -10,  5,  0,  0,  0,  0,  5,-10,
        -20,-10,-10,-10,-10,-10,-10,-20,
    };

    while (bishopBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(bishopBitboard);
        score += 330 + bishopStructure[i];
        bishopBitboard &= bishopBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateRookScore(ulong rookBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic rook structure value.
    int[] rookStructure = 
    {
        0,  0,  0,  0,  0,  0,  0,  0,
        5, 10, 10, 10, 10, 10, 10,  5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        0,  0,  0,  5,  5,  0,  0,  0
    };
    if (isWhite){ 
        Array.Reverse(rookStructure);
    }
    while (rookBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(rookBitboard);
        score += 500 + rookStructure[i];
        rookBitboard &= rookBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateQueenScore(ulong queenBitboard, bool isWhite) 
{
    int score = 0;
    // We define a basic queen structure value.
    int[] queenStructure = 
    {
        -20,-10,-10, -5, -5,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5,  5,  5,  5,  0,-10,
        -5,  0,  5,  5,  5,  5,  0, -5,
        0,  0,  5,  5,  5,  5,  0, -5,
        -10,  5,  5,  5,  5,  5,  0,-10,
        -10,  0,  5,  0,  0,  0,  0,-10,
        -20,-10,-10, -5, -5,-10,-10,-20
    };

    while (queenBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(queenBitboard);
        score += 900 + queenStructure[i];
        queenBitboard &= queenBitboard - 1; // clears least significant bit
    }

    return score;
}

public int CalculateKingScore(ulong kingBitboard, bool isWhite, bool isEndGame) 
{
    int score = 0;
    //we define a beginning/middle game king structure value
        int[] midKingStructure = 
    {
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -10,-20,-20,-20,-20,-20,-20,-10,
            20, 20,  0,  0,  0,  0, 20, 20,
            20, 30, 10,  0,  0, 10, 30, 20
    };
            int[] endKingStructure = 
    {
        -50,-40,-30,-20,-20,-30,-40,-50,
        -30,-20,-10,  0,  0,-10,-20,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-30,  0,  0,  0,  0,-30,-30,
        -50,-30,-30,-30,-30,-30,-30,-50
    };
    if (isWhite) {
        Array.Reverse(midKingStructure);
        Array.Reverse(endKingStructure);
    }
    int[] kingStructure = isEndGame ? endKingStructure : midKingStructure;
    while (kingBitboard != 0)
    {
        int i = BitOperations.TrailingZeroCount(kingBitboard);
        score += kingStructure[i];
        kingBitboard &= kingBitboard - 1; // clears least significant bit
    }
    return score;
}


}

public class MyIterDeepPSEV1 : IChessBot
{
    public int positionsEvaluated = 0;
    public int TIME_PER_MOVE =  1000;
    public Move Think(Board board, Timer timer)
    {
        // Move[] legalMoves = getMovesSorted(board);
        int numPieces = getNumPieces(board);
        int maxDepth = 10;
        int depthLeft = 1;
        Move bestMoveFinal = Move.NullMove;
        double bestEvalFinal = double.NegativeInfinity;
        Move bestMoveCurrent = Move.NullMove;
        double bestEvalCurrent = double.NegativeInfinity;
        if (timer.MillisecondsRemaining < 10000) {
            maxDepth = 7;
        }
        if (timer.MillisecondsRemaining < 6000) {
            maxDepth = 5;
        }
        if (timer.MillisecondsRemaining < 3000) {
            maxDepth = 4;
        }
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE && depthLeft < maxDepth) {
            //iterative deepening
            double alpha = double.NegativeInfinity;
            double beta = double.PositiveInfinity;
            (bestMoveCurrent, bestEvalCurrent) = NegaMax(board: board, depthLeft: depthLeft, 
                                                depthSoFar: 0, color: 1, alpha: alpha, beta: beta, 
                                                rootIsWhite: board.IsWhiteToMove, prevBestMove: bestMoveCurrent, timer: timer);
            if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
                break;
            }
            bestMoveFinal = bestMoveCurrent;
            bestEvalFinal = bestEvalCurrent;
            //Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}\n", bestMoveFinal, bestEvalFinal, depthLeft, positionsEvaluated);
            depthLeft++;
            
        }
    positionsEvaluated = 0;
    return bestMoveFinal;
    }
    public int getNumPieces(Board board) {
        int numPieces = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        foreach (PieceList pieceList in pieces) {
            numPieces += pieceList.Count;
        }
        return numPieces;
    }


public (Move, double) NegaMax(Board board, int depthLeft, int depthSoFar, int color, double alpha, double beta, 
                                bool rootIsWhite, Move prevBestMove, Timer timer)
{
    if (depthLeft == 0 || board.IsInCheckmate() || board.IsInsufficientMaterial() || board.IsRepeatedPosition() || 
                board.FiftyMoveCounter >= 100 || timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE)
    {
        return (Move.NullMove, color * EvaluateBoard(board, rootIsWhite, depthSoFar));
    }
    //put the best move from the previous iteration first in the list
    System.Span<Move> captures = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref captures, true);
    System.Span<Move> notCaptures = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref notCaptures, false);
    //remove any elements from notCaptures that are in captures
    for (int i = 0; i < captures.Length; i++) {
        for (int j = 0; j < notCaptures.Length; j++) {
            if (captures[i] == notCaptures[j]) {
                notCaptures[j] = notCaptures[notCaptures.Length - 1];
                notCaptures = notCaptures.Slice(0, notCaptures.Length - 1);
                break;
            }
        }
    }
    System.Span<Move> legalMoves = stackalloc Move[captures.Length + notCaptures.Length];
    captures.CopyTo(legalMoves);
    notCaptures.CopyTo(legalMoves.Slice(captures.Length));
    if (legalMoves.Length  == 0) { //stalemate detected
        return (Move.NullMove, 0);
    }
    if (prevBestMove != Move.NullMove) {
        for (int i = 0; i < legalMoves.Length; i++) {
            if (legalMoves[i] == prevBestMove) {
                Move temp = legalMoves[0];
                legalMoves[0] = legalMoves[i];
                legalMoves[i] = temp;
                break;
            }
        }
    }
    double maxEval = double.NegativeInfinity;
    Move bestMove = Move.NullMove;

    foreach (Move move in legalMoves)
    {
        board.MakeMove(move);
        double eval = -NegaMax(board: board, depthLeft: depthLeft - 1, depthSoFar: depthSoFar + 1, 
                            color: -color, alpha: -beta, beta: -alpha, rootIsWhite: rootIsWhite, Move.NullMove, timer).Item2;
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
    
    return (bestMove, maxEval);
}

        public delegate int CalculateScoreDelegate(ulong pieceBitboard, bool isWhite);
    public double EvaluateBoard(Board board, bool rootIsWhite, int depthSoFar) {
        PieceType[] pieceTypes = {PieceType.Pawn, PieceType.Bishop, PieceType.Knight, PieceType.Rook, PieceType.Queen};
        positionsEvaluated++;
        double whiteScore = 0;
        double blackScore = 0;
        if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100) {
                    return -1;
                }
        if (board.IsInCheckmate()) {
            if (board.IsWhiteToMove) {
                whiteScore -= 9999999999 - depthSoFar;
                }
            else {
                blackScore -= 9999999999 - depthSoFar;
                }
            if (rootIsWhite) {
                return whiteScore - blackScore;
                }   
            else {
                return blackScore - whiteScore;
            }
                }
        ulong[] whitePieceBitBoards = new ulong[5];
        ulong[] blackPieceBitBoards = new ulong[5];
                CalculateScoreDelegate[] calculationFunctions = 
        {
            CalculatePawnScore,
            CalculateBishopScore,
            CalculateKnightScore,
            CalculateRookScore,
            CalculateQueenScore
        };
        for (int i = 0; i < 5; i++) {
            whitePieceBitBoards[i] = board.GetPieceBitboard(pieceTypes[i], white: true);
            blackPieceBitBoards[i] = board.GetPieceBitboard(pieceTypes[i], white: false);
            whiteScore += calculationFunctions[i](whitePieceBitBoards[i], isWhite: true);
            blackScore += calculationFunctions[i](blackPieceBitBoards[i], isWhite: false);
        }
        
        int numPieces = getNumPieces(board);
        bool isEndGame = numPieces <= 12;
        whiteScore += CalculateKingScore(board.GetPieceBitboard(PieceType.King, white: true), isWhite: true, isEndGame: isEndGame);
        blackScore += CalculateKingScore(board.GetPieceBitboard(PieceType.King, white: false), isWhite: false, isEndGame: isEndGame);

        // //check castling
        // if (board.HasKingsideCastleRight(white: true)) {
        //     whiteScore += 20;
        // }
        // if (board.HasQueensideCastleRight(white: true)) {
        //     whiteScore += 20;
        // }
        // if (board.HasKingsideCastleRight(white: false)) {
        //     blackScore += 20;
        // }
        // if (board.HasQueensideCastleRight(white: false)) {
        //     blackScore += 20;
        // }
        if (rootIsWhite) {
            return whiteScore - blackScore;
        }
        else {
            return blackScore - whiteScore;
        }
}
public int CalculatePawnScore(ulong pawnBitboard, bool isWhite) 
{
    int score = 0;
    ulong bit = 1;
    // We define a basic pawn structure value.
    
    int[] pawnStructure = 
        {
            0,  0,  0,  0,  0,  0,  0,  0,
            50, 50, 50, 50, 50, 50, 50, 50,
            10, 10, 20, 30, 30, 20, 10, 10,
            5,  5, 10, 25, 25, 10,  5,  5,
            0,  0,  0, 20, 20,  0,  0,  0,
            5, -5,-10,  0,  0,-10, -5,  5,
            5, 10, 10,-20,-20, 10, 10,  5,
            0,  0,  0,  0,  0,  0,  0,  0
        };
    if (isWhite) 
    {
        Array.Reverse(pawnStructure);
    }


    
    for (int i = 0; i < 64; i++)
    {
        if ((pawnBitboard & bit) != 0)
        {
            score += 100 + pawnStructure[i];
        }
        bit <<= 1;
    }

    return score;
}
public int CalculateKnightScore(ulong knightBitboard, bool isWhite) 
{
    int score = 0;
    ulong bit = 1;
    // We define a basic knight structure value.
    int[] knightStructure = 
    {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50,
    };

    for (int i = 0; i < 64; i++)
    {
        if ((knightBitboard & bit) != 0)
        {
            score += 320  + knightStructure[i];
        }
        bit <<= 1;
    }

    return  score;
}

public int CalculateBishopScore(ulong bishopBitboard, bool isWhite) 
{
    int score = 0;
    ulong bit = 1;
    // We define a basic bishop structure value.
    int[] bishopStructure = 
    {
        -20,-10,-10,-10,-10,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5, 10, 10,  5,  0,-10,
        -10,  5,  5, 10, 10,  5,  5,-10,
        -10,  0, 10, 10, 10, 10,  0,-10,
        -10, 10, 10, 10, 10, 10, 10,-10,
        -10,  5,  0,  0,  0,  0,  5,-10,
        -20,-10,-10,-10,-10,-10,-10,-20,
    };

    for (int i = 0; i < 64; i++)
    {
        if ((bishopBitboard & bit) != 0)
        {
            score += 330 + bishopStructure[i];
        }
        bit <<= 1;
    }

    return score;
}

public int CalculateRookScore(ulong rookBitboard, bool isWhite) 
{
    int score = 0;
    ulong bit = 1;
    // We define a basic rook structure value.
    int[] rookStructure = 
    {
        0,  0,  0,  0,  0,  0,  0,  0,
        5, 10, 10, 10, 10, 10, 10,  5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        0,  0,  0,  5,  5,  0,  0,  0
    };
    if (isWhite){ 
        Array.Reverse(rookStructure);
    }
    for (int i = 0; i < 64; i++)
    {
        if ((rookBitboard & bit) != 0)
        {
            score += 500 + rookStructure[i];
        }
        bit <<= 1;
    }

    return score;
}

public int CalculateQueenScore(ulong queenBitboard, bool isWhite) 
{
    int score = 0;
    ulong bit = 1;
    // We define a basic queen structure value.
    int[] queenStructure = 
    {
        -20,-10,-10, -5, -5,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5,  5,  5,  5,  0,-10,
        -5,  0,  5,  5,  5,  5,  0, -5,
        0,  0,  5,  5,  5,  5,  0, -5,
        -10,  5,  5,  5,  5,  5,  0,-10,
        -10,  0,  5,  0,  0,  0,  0,-10,
        -20,-10,-10, -5, -5,-10,-10,-20
    };

    for (int i = 0; i < 64; i++)
    {
        if ((queenBitboard & bit) != 0)
        {
            score += 900 + queenStructure[i];
        }
        bit <<= 1;
    }

    return score;
}

public int CalculateKingScore(ulong kingBitboard, bool isWhite, bool isEndGame) 
{
    int score = 0;
    ulong bit = 1;
    //we define a beginning/middle game king structure value
        int[] midKingStructure = 
    {
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -10,-20,-20,-20,-20,-20,-20,-10,
            20, 20,  0,  0,  0,  0, 20, 20,
            20, 30, 10,  0,  0, 10, 30, 20
    };
            int[] endKingStructure = 
    {
        -50,-40,-30,-20,-20,-30,-40,-50,
        -30,-20,-10,  0,  0,-10,-20,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-30,  0,  0,  0,  0,-30,-30,
        -50,-30,-30,-30,-30,-30,-30,-50
    };
    if (isWhite) {
        Array.Reverse(midKingStructure);
        Array.Reverse(endKingStructure);
    }
    int[] kingStructure = isEndGame ? endKingStructure : midKingStructure;
    for (int i = 0; i < 64; i++)
    {
        if ((kingBitboard & bit) != 0)
        {
            score +=  kingStructure[i];
        }
        bit <<= 1;
    }
    return score;
}


}

public class MyIterDeepV1 : IChessBot
{
    public int TIME_PER_MOVE =  1000;
    public Move Think(Board board, Timer timer)
    {
        // Move[] legalMoves = getMovesSorted(board);
        int numPieces = getNumPieces(board);
        int maxDepth = 150 / numPieces;
        int depthLeft = 1;
        Move bestMoveFinal = Move.NullMove;
        double bestEvalFinal = double.NegativeInfinity;
        Move bestMoveCurrent = Move.NullMove;
        double bestEvalCurrent = double.NegativeInfinity;
        if (timer.MillisecondsRemaining < 10000) {
            maxDepth = 7;
        }
        if (timer.MillisecondsRemaining < 5000) {
            maxDepth = 5;
        }
        while (timer.MillisecondsElapsedThisTurn < TIME_PER_MOVE && depthLeft < maxDepth) {
            //iterative deepening
            double alpha = double.NegativeInfinity;
            double beta = double.PositiveInfinity;
            (bestMoveCurrent, bestEvalCurrent) = NegaMax(board: board, depthLeft: depthLeft, 
                                                depthSoFar: 0, color: -1, alpha: alpha, beta: beta, 
                                                rootIsWhite: !board.IsWhiteToMove, prevBestMove: bestMoveCurrent, timer: timer);
            if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {
                break;
            }
            bestMoveFinal = bestMoveCurrent;
            bestEvalFinal = bestEvalCurrent;
            //Console.Write("best move: {0}, value: {1}, depth: {2}\n", bestMoveFinal, bestEvalFinal, depthLeft);
            depthLeft++;
            
        }
    return bestMoveFinal;
    }
    public int getNumPieces(Board board) {
        int numPieces = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        foreach (PieceList pieceList in pieces) {
            numPieces += pieceList.Count;
        }
        return numPieces;
    }


public (Move, double) NegaMax(Board board, int depthLeft, int depthSoFar, int color, double alpha, double beta, 
                                bool rootIsWhite, Move prevBestMove, Timer timer)
{
    if (depthLeft == 0 || board.IsInCheckmate() || board.IsInsufficientMaterial() || board.IsRepeatedPosition() || 
                board.FiftyMoveCounter >= 100 || timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE)
    {
        return (Move.NullMove, color * EvaluateBoard(board, rootIsWhite, depthSoFar));
    }
    //put the best move from the previous iteration first in the list
    System.Span<Move> legalMoves = stackalloc Move[256];
    board.GetLegalMovesNonAlloc(ref legalMoves);
    if (legalMoves.Length  == 0) { //stalemate detected
        return (Move.NullMove, 0);
    }
    if (prevBestMove != Move.NullMove) {
        for (int i = 0; i < legalMoves.Length; i++) {
            if (legalMoves[i] == prevBestMove) {
                Move temp = legalMoves[0];
                legalMoves[0] = legalMoves[i];
                legalMoves[i] = temp;
                break;
            }
        }
    }
    double maxEval = double.NegativeInfinity;
    Move bestMove = Move.NullMove;

    foreach (Move move in legalMoves)
    {
        board.MakeMove(move);
        double eval = -NegaMax(board: board, depthLeft: depthLeft - 1, depthSoFar: depthSoFar + 1, 
                            color: -color, alpha: -beta, beta: -alpha, rootIsWhite: rootIsWhite, Move.NullMove, timer).Item2;
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
    
    return (bestMove, maxEval);
}



    public double EvaluateBoard(Board board, bool rootIsWhite, int depthSoFar) {
        double whiteScore = 0;
        double blackScore = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        if (board.IsInsufficientMaterial() || board.IsRepeatedPosition() || board.FiftyMoveCounter >= 100) {
            return -1;
        }
        if (board.IsInCheckmate()) {
            if (board.IsWhiteToMove) {
                whiteScore -= 9999999999 - depthSoFar;
                }
            else {
                blackScore -= 9999999999 - depthSoFar;
                }
            if (rootIsWhite) {
                return whiteScore - blackScore;
                }   
            else {
                return blackScore - whiteScore;
            }
        }
        foreach (PieceList pieceList in pieces) {
            if (pieceList.TypeOfPieceInList == PieceType.Pawn) { //pawn
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 100 * pieceList.Count;
                    // get the pawns square
                    for (int i = 0; i < pieceList.Count; i++) {
                        int rank = pieceList.GetPiece(i).Square.Rank;
                        int file = pieceList.GetPiece(i).Square.File;
                        // if pawn is pushed its worth more
                        whiteScore += (rank >= 3 && rank <= 7) ? 1 << (rank - 1) : 0;
                        //if pawn is on e2 or d2 -20
                        if (rank == 1 && (file == 4 ||  file == 3)) {
                            whiteScore -= 50;
                        }
                    }
                }
                else {
                    blackScore += 100 * pieceList.Count;
                    // get the pawns square
                    for (int i = 0; i < pieceList.Count; i++) {
                        int rank = pieceList.GetPiece(i).Square.Rank;
                        int file = pieceList.GetPiece(i).Square.File;
                        // if pawn is pushed its worth more
                        blackScore += (rank >= 2 && rank <= 6) ? 1 << (7 - rank) : 0;
                        //if pawn is on e7 or d7 -20
                        if (rank == 6 && (file == 4 ||  file == 3)) {
                            blackScore -= 50;
                        }
                    }
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Knight) { //knight
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 300 * pieceList.Count;
                }
                else {
                    blackScore += 300 * pieceList.Count;
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Bishop) { //bishop
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 320 * pieceList.Count;
                }
                else {
                    blackScore += 320 * pieceList.Count;
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Rook) { //rook
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 500 * pieceList.Count;
                }
                else {
                    blackScore += 500 * pieceList.Count;
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Queen) { //queen
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 900 * pieceList.Count;
                }
                else {
                    blackScore += 900 * pieceList.Count;
                }
            }
        }
        // if (board.IsInCheck()) { //search extension if in check
        //     if (board.IsWhiteToMove) {
        //         // new timer 
        //         Timer tempTimer = new Timer(500);
        //         //tempColor = -1 if root is white
        //         int tempColor = rootIsWhite ? -1 : 1;
        //         return NegaMax(board: board, depthLeft: 1, depthSoFar: 0, color: tempColor, alpha: double.NegativeInfinity, beta: double.PositiveInfinity, 
        //         rootIsWhite: rootIsWhite, Move.NullMove, tempTimer).Item2;
        //     }
        //     else {
        //         Timer tempTimer = new Timer(500);
        //         int tempColor = rootIsWhite ? 1 : -1;
        //         return NegaMax(board: board, depthLeft: 1, depthSoFar: 0, color: tempColor, alpha: double.NegativeInfinity, beta: double.PositiveInfinity, 
        //         rootIsWhite: rootIsWhite, Move.NullMove, tempTimer).Item2;
        //     }
        // }
        if (rootIsWhite) {
            return whiteScore - blackScore;
        }
        else {
            return blackScore - whiteScore;
        }
    }

    
}

public class MyABNegamaxV2 : IChessBot
{
    public const int TIME_PER_MOVE = 2500;
    public const int INITIAL_DEPTH = 4;
    public int turns = 0;
    public Move Think(Board board, Timer timer)
    {
        // Move[] moves = board.GetLegalMoves();
        // return moves[0];

        // Move[] legalMoves = board.GetLegalMoves();
        // Move[] captures = board.GetLegalMoves(capturesOnly: true);
        // //randomly shuffle legal moves
        // Random rnd = new Random();
        // legalMoves = legalMoves.OrderBy(x => rnd.Next()).ToArray();
        Move[] legalMoves = getMovesSorted(board);

        int num_legal_moves = legalMoves.Length;
        //set a random move as best to start
        Move bestMove = legalMoves[0];
        double bestValue = double.NegativeInfinity;
        int boardPieces = getNumPieces(board);
        int depthLeft = INITIAL_DEPTH;
        if (boardPieces <= 16){
            depthLeft++;
        }
        if (boardPieces <= 12) {
            depthLeft++;
        }
        if (boardPieces <= 8) {
            depthLeft++;
        }
        if (boardPieces <= 6) {
            depthLeft++;
        }
        if (boardPieces <= 4) {
            depthLeft++;
        }
        if (timer.MillisecondsRemaining < 30000) {
            depthLeft--;
        }
        if (timer.MillisecondsRemaining < 15000) {
            depthLeft--;
        }
        if (timer.MillisecondsRemaining < 10000) {
            depthLeft--;
        }
        if (timer.MillisecondsRemaining < 5000) {
            depthLeft--;
        }
        if (num_legal_moves >= 25) {
            depthLeft--;
        }
        int movesChecked = 0;
        bool Reduced = false;
        double alpha = double.NegativeInfinity;
        double beta = double.PositiveInfinity;
        foreach (Move move in legalMoves) 
            {
                if (timer.MillisecondsElapsedThisTurn > TIME_PER_MOVE && Reduced == false) {
                    depthLeft -= 2;
                    Reduced = true;
                }
                board.MakeMove(move);
                //double boardValue = Minimax(board, depthLeft, 0, false, !board.IsWhiteToMove, timer, double.NegativeInfinity, double.PositiveInfinity);
                double boardValue = -NegaMax(board: board, depthLeft: depthLeft, depthSoFar: 0, color: -1, timer: timer, alpha: -beta, beta: -alpha, !board.IsWhiteToMove);
                if (boardValue > bestValue) 
                {
                    bestValue = boardValue;
                    bestMove = move;
                    alpha = Math.Max(alpha, bestValue);
                }
                board.UndoMove(move);
                movesChecked++;
                //Console.Write("Moves checked: {0} / {1}, Depth: {2}, current position strength: {3}\n", movesChecked, legalMoves.Length, depthLeft, bestValue);
                // Console.Write("\rBest value is: {0}", bestValue);
                // Console.Write("\rTime elapsed: {0}", timer.MillisecondsElapsedThisTurn);
                // if (bestValue > 1000000) {
                //     break;
                // }
            }
    // Console.WriteLine("Moves checked: {0} / {1}, Depth: {2}", movesChecked, legalMoves.Length, depth);
    // Console.WriteLine("Best value is: " + bestValue);
    // Console.WriteLine("Time elapsed: " + timer.MillisecondsElapsedThisTurn);
    turns++;
    return bestMove;
    }
    public int getNumPieces(Board board) {
        int numPieces = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        foreach (PieceList pieceList in pieces) {
            numPieces += pieceList.Count;
        }
        return numPieces;
    }


public Move[] getMovesSorted(Board board) {
        Move[] captureMoves = board.GetLegalMoves(capturesOnly: true);
        Move[] nonCaptureMoves = board.GetLegalMoves(capturesOnly: false).Except(captureMoves).ToArray();
        //randomly shuffle capture moves and non capture moves
        Random rnd = new Random();
        captureMoves = captureMoves.OrderBy(x => rnd.Next()).ToArray();
        nonCaptureMoves = nonCaptureMoves.OrderBy(x => rnd.Next()).ToArray();
        //create legalMoves array, with captures first then non captures
        Move[] legalMoves = new Move[captureMoves.Length + nonCaptureMoves.Length];
        Array.Copy(captureMoves, legalMoves, captureMoves.Length);
        Array.Copy(nonCaptureMoves, 0, legalMoves, captureMoves.Length, nonCaptureMoves.Length);
        return legalMoves;
}

public double NegaMax(Board board, int depthLeft, int depthSoFar, int color, Timer timer, double alpha, double beta, bool rootIsWhite)
{
    if (depthLeft == 0 || board.IsInCheckmate() || board.IsDraw())
    {
        return color * EvaluateBoard(board, rootIsWhite, depthSoFar);
    }

    Move[] legalMoves = getMovesSorted(board);
    double maxEval = double.NegativeInfinity;

    foreach (Move move in legalMoves)
    {
        board.MakeMove(move);
        double eval = -NegaMax(board: board, depthLeft: depthLeft - 1, depthSoFar: depthSoFar + 1, color: -color, timer: timer, alpha: -beta, beta: -alpha, rootIsWhite: rootIsWhite);
        board.UndoMove(move);
        maxEval = Math.Max(eval, maxEval);
        alpha = Math.Max(alpha, maxEval);
        if (alpha >= beta) {
            break;
        }
    }
    
    return maxEval;
}


    public double EvaluateBoard(Board board, bool rootIsWhite, int depthSoFar) {
        double whiteScore = 0;
        double blackScore = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        if (board.IsDraw()) {
            return -1;
        }
        if (board.IsInCheckmate()) {
            if (board.IsWhiteToMove) {
                whiteScore -= 9999999999 - depthSoFar;
                }
            else {
                blackScore -= 9999999999 - depthSoFar;
                }
            if (rootIsWhite) {
                return whiteScore - blackScore;
                }   
            else {
                return blackScore - whiteScore;
            }
        }
        // if (board.IsWhiteToMove) {
        //     whiteScore += board.GetLegalMoves().Length;
        //     board.MakeMove(Move.NullMove);
        //     blackScore += board.GetLegalMoves().Length;
        //     board.UndoMove(Move.NullMove);

        // }
        // else {
        //     blackScore += board.GetLegalMoves().Length;
        //     board.MakeMove(Move.NullMove);
        //     whiteScore += board.GetLegalMoves().Length;  
        //     board.UndoMove(Move.NullMove);
        // }
        foreach (PieceList pieceList in pieces) {
            if (pieceList.TypeOfPieceInList == PieceType.Pawn) { //pawn
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 100 * pieceList.Count;
                    // get the pawns square
                    for (int i = 0; i < pieceList.Count; i++) {
                        int rank = pieceList.GetPiece(i).Square.Rank;
                        // if pawn is pushed its worth more
                        whiteScore += (rank >= 3 && rank <= 7) ? 1 << (rank - 1) : 0;
                    }
                }
                else {
                    blackScore += 100 * pieceList.Count;
                    // get the pawns square
                    for (int i = 0; i < pieceList.Count; i++) {
                        int rank = pieceList.GetPiece(i).Square.Rank;
                        // if pawn is pushed its worth more
                        blackScore += (rank >= 2 && rank <= 6) ? 1 << (7 - rank) : 0;
                    }
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Knight) { //knight
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 300 * pieceList.Count;
                }
                else {
                    blackScore += 300 * pieceList.Count;
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Bishop) { //bishop
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 320 * pieceList.Count;
                }
                else {
                    blackScore += 320 * pieceList.Count;
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Rook) { //rook
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 500 * pieceList.Count;
                }
                else {
                    blackScore += 500 * pieceList.Count;
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Queen) { //queen
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 900 * pieceList.Count;
                }
                else {
                    blackScore += 900 * pieceList.Count;
                }
            }
        }
        if (board.IsInCheck()) {
            if (board.IsWhiteToMove) {
                whiteScore -= 200;
            }
            else {
                blackScore -= 200;
            }
        }
        if (rootIsWhite) {
            return whiteScore - blackScore;
        }
        else {
            return blackScore - whiteScore;
        }
    }
}

public class MyABNegamax : IChessBot
{
    public const int TIME_PER_MOVE = 2500;
    public const int INITIAL_DEPTH = 4;
    public int turns = 0;
    public Move Think(Board board, Timer timer)
    {
        // Move[] moves = board.GetLegalMoves();
        // return moves[0];

        // Move[] legalMoves = board.GetLegalMoves();
        // Move[] captures = board.GetLegalMoves(capturesOnly: true);
        // //randomly shuffle legal moves
        // Random rnd = new Random();
        // legalMoves = legalMoves.OrderBy(x => rnd.Next()).ToArray();
        Move[] legalMoves = getMovesSorted(board);

        int num_legal_moves = legalMoves.Length;
        //set a random move as best to start
        Move bestMove = legalMoves[0];
        double bestValue = double.NegativeInfinity;
        int boardPieces = getNumPieces(board);
        int depthLeft = INITIAL_DEPTH;
        if (boardPieces <= 16){
            depthLeft++;
        }
        if (boardPieces <= 12) {
            depthLeft++;
        }
        if (boardPieces <= 8) {
            depthLeft++;
        }
        if (boardPieces <= 6) {
            depthLeft++;
        }
        if (boardPieces <= 4) {
            depthLeft++;
        }
        if (timer.MillisecondsRemaining < 30000) {
            depthLeft--;
        }
        if (timer.MillisecondsRemaining < 15000) {
            depthLeft--;
        }
        if (timer.MillisecondsRemaining < 10000) {
            depthLeft--;
        }
        if (timer.MillisecondsRemaining < 5000) {
            depthLeft--;
        }
        if (num_legal_moves >= 25) {
            depthLeft--;
        }
        int movesChecked = 0;
        bool Reduced = false;
        double alpha = double.NegativeInfinity;
        double beta = double.PositiveInfinity;
        foreach (Move move in legalMoves) 
            {
                if (timer.MillisecondsElapsedThisTurn > TIME_PER_MOVE && Reduced == false) {
                    depthLeft -= 2;
                    Reduced = true;
                }
                board.MakeMove(move);
                //double boardValue = Minimax(board, depthLeft, 0, false, !board.IsWhiteToMove, timer, double.NegativeInfinity, double.PositiveInfinity);
                double boardValue = -NegaMax(board: board, depthLeft: depthLeft, depthSoFar: 0, color: -1, timer: timer, alpha: -beta, beta: -alpha, !board.IsWhiteToMove);
                if (boardValue > bestValue) 
                {
                    bestValue = boardValue;
                    bestMove = move;
                    alpha = Math.Max(alpha, bestValue);
                }
                board.UndoMove(move);
                movesChecked++;
                //Console.Write("Moves checked: {0} / {1}, Depth: {2}, current position strength: {3}\n", movesChecked, legalMoves.Length, depthLeft, bestValue);
                // Console.Write("\rBest value is: {0}", bestValue);
                // Console.Write("\rTime elapsed: {0}", timer.MillisecondsElapsedThisTurn);
                // if (bestValue > 1000000) {
                //     break;
                // }
            }
    // Console.WriteLine("Moves checked: {0} / {1}, Depth: {2}", movesChecked, legalMoves.Length, depth);
    // Console.WriteLine("Best value is: " + bestValue);
    // Console.WriteLine("Time elapsed: " + timer.MillisecondsElapsedThisTurn);
    turns++;
    return bestMove;
    }
    public int getNumPieces(Board board) {
        int numPieces = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        foreach (PieceList pieceList in pieces) {
            numPieces += pieceList.Count;
        }
        return numPieces;
    }


public Move[] getMovesSorted(Board board) {
        Move[] captureMoves = board.GetLegalMoves(capturesOnly: true);
        Move[] nonCaptureMoves = board.GetLegalMoves(capturesOnly: false).Except(captureMoves).ToArray();
        //randomly shuffle capture moves and non capture moves
        Random rnd = new Random();
        captureMoves = captureMoves.OrderBy(x => rnd.Next()).ToArray();
        nonCaptureMoves = nonCaptureMoves.OrderBy(x => rnd.Next()).ToArray();
        //create legalMoves array, with captures first then non captures
        Move[] legalMoves = new Move[captureMoves.Length + nonCaptureMoves.Length];
        Array.Copy(captureMoves, legalMoves, captureMoves.Length);
        Array.Copy(nonCaptureMoves, 0, legalMoves, captureMoves.Length, nonCaptureMoves.Length);
        return legalMoves;
}

public double NegaMax(Board board, int depthLeft, int depthSoFar, int color, Timer timer, double alpha, double beta, bool rootIsWhite)
{
    if (depthLeft == 0 || board.IsInCheckmate() || board.IsDraw())
    {
        return color * EvaluateBoard(board, rootIsWhite, depthSoFar);
    }

    Move[] legalMoves = getMovesSorted(board);
    double maxEval = double.NegativeInfinity;

    foreach (Move move in legalMoves)
    {
        board.MakeMove(move);
        double eval = -NegaMax(board: board, depthLeft: depthLeft - 1, depthSoFar: depthSoFar + 1, color: -color, timer: timer, alpha: -beta, beta: -alpha, rootIsWhite: rootIsWhite);
        board.UndoMove(move);
        maxEval = Math.Max(eval, maxEval);
        alpha = Math.Max(alpha, maxEval);
        if (alpha >= beta) {
            break;
        }
    }
    
    return maxEval;
}


    public double EvaluateBoard(Board board, bool rootIsWhite, int depthSoFar) {
        double whiteScore = 0;
        double blackScore = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        if (board.IsDraw()) {
            return -1;
        }
        if (board.IsInCheckmate()) {
            if (board.IsWhiteToMove) {
                whiteScore -= 9999999999 - depthSoFar;
                }
            else {
                blackScore -= 9999999999 - depthSoFar;
                }
            if (rootIsWhite) {
                return whiteScore - blackScore;
                }   
            else {
                return blackScore - whiteScore;
            }
        }
        foreach (PieceList pieceList in pieces) {
            if (pieceList.TypeOfPieceInList == PieceType.Pawn) { //pawn
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 100 * pieceList.Count;
                    // get the pawns square
                    for (int i = 0; i < pieceList.Count; i++) {
                        int rank = pieceList.GetPiece(i).Square.Rank;
                        // if pawn is pushed its worth more
                        whiteScore += (rank >= 3 && rank <= 7) ? 1 << (rank - 1) : 0;
                    }
                }
                else {
                    blackScore += 100 * pieceList.Count;
                    // get the pawns square
                    for (int i = 0; i < pieceList.Count; i++) {
                        int rank = pieceList.GetPiece(i).Square.Rank;
                        // if pawn is pushed its worth more
                        blackScore += (rank >= 2 && rank <= 6) ? 1 << (7 - rank) : 0;
                    }
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Knight) { //knight
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 300 * pieceList.Count;
                }
                else {
                    blackScore += 300 * pieceList.Count;
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Bishop) { //bishop
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 300 * pieceList.Count;
                }
                else {
                    blackScore += 300 * pieceList.Count;
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Rook) { //rook
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 500 * pieceList.Count;
                }
                else {
                    blackScore += 500 * pieceList.Count;
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Queen) { //queen
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 900 * pieceList.Count;
                }
                else {
                    blackScore += 900 * pieceList.Count;
                }
            }
        }
        if (board.IsInCheck()) {
            if (board.IsWhiteToMove) {
                whiteScore -= 200;
            }
            else {
                blackScore -= 200;
            }
        }
        if (rootIsWhite) {
            return whiteScore - blackScore;
        }
        else {
            return blackScore - whiteScore;
        }
    }
}

    // A simple bot that can spot mate in one, and always captures the most valuable piece it can.
    // Plays randomly otherwise.
    public class EvilBot : IChessBot
    {
        // Piece values: null, pawn, knight, bishop, rook, queen, king
        int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 };

        public Move Think(Board board, Timer timer)
        {
            Move[] allMoves = board.GetLegalMoves();

            // Pick a random move to play if nothing better is found
            Random rng = new();
            Move moveToPlay = allMoves[rng.Next(allMoves.Length)];
            int highestValueCapture = 0;

            foreach (Move move in allMoves)
            {
                // Always play checkmate in one
                if (MoveIsCheckmate(board, move))
                {
                    moveToPlay = move;
                    break;
                }

                // Find highest value capture
                Piece capturedPiece = board.GetPiece(move.TargetSquare);
                int capturedPieceValue = pieceValues[(int)capturedPiece.PieceType];

                if (capturedPieceValue > highestValueCapture)
                {
                    moveToPlay = move;
                    highestValueCapture = capturedPieceValue;
                }
            }

            return moveToPlay;
        }

        // Test if this move gives checkmate
        bool MoveIsCheckmate(Board board, Move move)
        {
            board.MakeMove(move);
            bool isMate = board.IsInCheckmate();
            board.UndoMove(move);
            return isMate;
        }
    }


public class MyNaiveMinimax : IChessBot
{
    public const int TIME_PER_MOVE = 2500;
    public int turns = 0;
    public Move Think(Board board, Timer timer)
    {
        // Move[] moves = board.GetLegalMoves();
        // return moves[0];

        Move[] legalMoves = board.GetLegalMoves();
        int num_legal_moves = legalMoves.Length;
        //set a random move as best to start
        Random rnd = new Random();
        int randomIndex = rnd.Next(legalMoves.Length);
        Move bestMove = legalMoves[randomIndex];
        double bestValue = double.NegativeInfinity;
        int boardPieces = getNumPieces(board);
        int depth = 3;
        int movesChecked = 0;
        foreach (Move move in legalMoves) 
            {
                if (timer.MillisecondsElapsedThisTurn > TIME_PER_MOVE) {
                    break;
                }
                board.MakeMove(move);
                double boardValue = Minimax(board, depth - 1, false, !board.IsWhiteToMove, timer);
                
                if (boardValue > bestValue) 
                {
                    bestValue = boardValue;
                    bestMove = move;
                }
                board.UndoMove(move);
                movesChecked++;
            }
    turns++;
    return bestMove;
    }
    public int getNumPieces(Board board) {
        int numPieces = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        foreach (PieceList pieceList in pieces) {
            numPieces += pieceList.Count;
        }
        return numPieces;
    }
    public double Minimax(Board board, int depth, bool isMaximizingPlayer, bool rootIsWhite, Timer timer)  {
        if (depth == 0 || board.IsInCheckmate() || board.IsDraw() || timer.MillisecondsElapsedThisTurn > TIME_PER_MOVE)
        {
            return EvaluateBoard(board, rootIsWhite);
        }

        Move[] legalMoves = board.GetLegalMoves();
        
        if (isMaximizingPlayer) 
        {
            double maxEval = double.NegativeInfinity;
            
            foreach (Move move in legalMoves) 
            {
                board.MakeMove(move);
                double eval = Minimax(board, depth - 1, false, rootIsWhite, timer);
                maxEval = Math.Max(maxEval, eval);
                board.UndoMove(move);
            }
            
            return maxEval;
        } 
        else 
        {
            double minEval = double.PositiveInfinity;
            
            foreach (Move move in legalMoves) 
            {
                board.MakeMove(move);
                double eval = Minimax(board, depth - 1, true, rootIsWhite, timer);
                minEval = Math.Min(minEval, eval);
                board.UndoMove(move);
            }
            
            return minEval;
        }
    }
    // You'll need to implement this function. The better the board is for us, the higher the score.
    public double EvaluateBoard(Board board, bool rootIsWhite) {
        int whiteScore = 0;
        int blackScore = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        foreach (PieceList pieceList in pieces) {
            if (pieceList.TypeOfPieceInList == PieceType.Pawn) { //pawn
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 100 * pieceList.Count;
                    // get the pawns square
                    for (int i = 0; i < pieceList.Count; i++) {
                        int rank = pieceList.GetPiece(i).Square.Rank;
                        // if pawn is pushed its worth more
                        if (rank == 7) {
                            whiteScore += 128;
                        }
                        else if (rank == 6) {
                            whiteScore += 32;
                        }
                        else if (rank == 5) {
                            whiteScore += 16;
                        }
                        else if (rank == 4) {
                            whiteScore += 8;
                        }
                        else if (rank == 3) {
                            whiteScore += 4;
                        }
                    }
                }
                else {
                    blackScore += 100 * pieceList.Count;
                    // get the pawns square
                    for (int i = 0; i < pieceList.Count; i++) {
                        int rank = pieceList.GetPiece(i).Square.Rank;
                        // if pawn is pushed its worth more
                        if (rank == 2) {
                            blackScore += 128;
                        }
                        else if (rank == 3) {
                            blackScore += 32;
                        }
                        else if (rank == 4) {
                            blackScore += 16;
                        }
                        else if (rank == 5) {
                            blackScore += 8;
                        }
                        else if (rank == 6) {
                            blackScore += 4;
                        }
                    }
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Knight) { //knight
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 300 * pieceList.Count;
                }
                else {
                    blackScore += 300 * pieceList.Count;
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Bishop) { //bishop
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 300 * pieceList.Count;
                }
                else {
                    blackScore += 300 * pieceList.Count;
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Rook) { //rook
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 500 * pieceList.Count;
                }
                else {
                    blackScore += 500 * pieceList.Count;
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Queen) { //queen
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 900 * pieceList.Count;
                }
                else {
                    blackScore += 900 * pieceList.Count;
                }
            }
        }
        if (board.IsDraw()) {
            return -1;
        }
        if (board.IsInCheckmate()) {
            if (board.IsWhiteToMove) {
                whiteScore -= 999999999;
            }
            else {
                blackScore -= 999999999;
            }
        }
        if (rootIsWhite) {
            return whiteScore - blackScore;
        }
        else {
            return blackScore - whiteScore;
        }
    }
}

public class DiscordMLBot : IChessBot
{
    //GLOBAL variables
    ushort searchDepth = 3;
    ushort turnIndex = 0;

    // a dictionary where the key value pairs are  <FEN,move(Eq."a2a4")>
    string[] openings = new string[] { 
        //BLACK OPENINGS
        "d7d5",
        //WHITE OPENINGS
        "e2e4"
    };

    string[] centerSquares = new string[] {
        "c6","d6","e6","f6",
        "c5","d5","e5","f5",
        "c4","d4","e4","f4",
        "c3","d3","e3","f3"
    };
    int[] preferedWhitePawnRows = new int[] {
        5,6,7
    };
    int[] preferedBlackPawnRows = new int[] {
        0,1,2
    };


    // Piece values: null, pawn, knight, bishop, rook, queen, king
    ushort[] pieceValues = { 100, 300, 300, 450, 950 };

    public Move Think(Board board, Timer timer)
    {
        Move[] moves = board.GetLegalMoves();

        Move chosenMove = GetMove(board, turnIndex);
        turnIndex++;

        return chosenMove;
    }

    private short GetBoardValue(Board board)
    {
        // Add up all white pieces value
        // Substract all black pieces value
        // return value if we are white, else return the opposite number

        short value = 0;

        PieceList[] pieceLists = board.GetAllPieceLists();

        for (ushort i = 0; i < pieceLists.Length-2; i++)
        {
            if (i < 5)
            {
                value +=(short)(pieceValues[i % 5] * pieceLists[i].Count);
            }
            else
            {
                value -=(short)(pieceValues[i % 5] * pieceLists[i+1].Count);

            }
        }

        if (board.IsWhiteToMove)
            return value;
        else
        {
            return (short)(-value);
        }

    }
    private short GetDirectMoveValue(Board board, Move move)
    {
        short value = 0;

        board.MakeMove(move);
        //  if the move is a check mate we choose this one
        if (board.IsInCheckmate())
        {
            value += 25000;
        }
        //check yields a higher value then pawn moves
        if (board.IsInCheck())
        {
            value += 28;
        }
        if (!board.IsInCheck()&&board.GetLegalMoves().Length==0)
        {
            value -= 25000;
        }

        board.UndoMove(move);

        Square targetSquare = move.TargetSquare;

        switch (board.GetPiece(move.StartSquare).PieceType)
        {
            case PieceType.Pawn:
                value += 11;
                if (board.IsWhiteToMove)
                {
                    if (preferedWhitePawnRows.Contains(targetSquare.Rank))
                    {
                        value += 16;
                    }
                }
                else
                {
                    if (preferedBlackPawnRows.Contains(targetSquare.Rank))
                    {
                        value += 16;
                    }
                }
                break;
            case PieceType.Knight:
                if (centerSquares.Contains(targetSquare.Name))
                {
                    value += 9;
                }
                break;
            case PieceType.Bishop:
                if (centerSquares.Contains(targetSquare.Name))
                {
                    value += 4;
                }
                break;
            case PieceType.Rook:
                if (centerSquares.Contains(targetSquare.Name))
                {
                    value += 2;
                }
                break;
            case PieceType.Queen:
                if (centerSquares.Contains(targetSquare.Name))
                {
                    value += 2;
                }
                break;
            case PieceType.King:
                value -= 22;
                if (move.IsCastles)
                {
                    value += 88;
                }
                break;
            default:
                break;
        }
        // we like capture trades
        if (move.IsCapture)
        {
            if (GetBoardValue(board)>=0)
            {
                value += 31;
            }
            else
            {
                value -= 20;
            }
        }

        return value;
    }

    private Move GetMove(Board board,int turn)
    {
        List<Move> bestMoves = new List<Move>();
        //We get the best moves through minmax algorithm
        GetMoveEval(board, searchDepth, -99999, 99999, true, bestMoves);

        foreach (string opening in openings)
        {
            if (bestMoves.Contains(new Move(opening, board)))
            {
                return new Move(opening,board);
            }
        }

        Move bestMove = bestMoves[0];
        short bestMoveValue = GetDirectMoveValue(board, bestMove);
        foreach (Move move in bestMoves)
        {
            short value = GetDirectMoveValue(board, move);
            if (value > bestMoveValue)
            {
                bestMove = move;
                bestMoveValue = value;
            }
        }


        return bestMove;
    }

    private short GetMoveEval(Board board, int depth, int alpha, int beta, bool isMaximizingPlayer, List<Move> bestMoves)
    {
        //NOTE : needs working implementation of the pruning algorithm
        //Chess version of the MINMAX algorithm 

        Move[] moves = board.GetLegalMoves();

        if (isMaximizingPlayer)
        {
            short maxEval = -30000;
            foreach (Move move in moves)
            {
                board.MakeMove(move);
                short eval = 0;
                eval = GetMoveEval(board, depth - 1, alpha, beta, false, bestMoves);

                if (eval > maxEval)
                {
                    maxEval = eval;
                    if (depth == searchDepth)
                    {
                        bestMoves.Clear();
                        bestMoves.Add(move);
                    }
                }
                else if (depth == searchDepth && eval == maxEval)
                {
                    bestMoves.Add(move);
                }

                board.UndoMove(move);
                // Attempt to alpha beta pruning (NOT WORKING YET)
                /*
                if (eval > alpha)
                {
                    alpha = eval;
                }
                if (beta <= alpha)
                {
                    break;
                }
                */
            }
            return maxEval;
        }
        else
        {
            short minEval = 30000;
            foreach (Move move in moves)
            {
                board.MakeMove(move);
                short eval = 0;
                if (depth > 0)
                {
                    eval = GetMoveEval(board, depth - 1, alpha, beta, true, bestMoves);
                }
                else
                {
                    eval = GetBoardValue(board);

                }
                if (eval < minEval)
                {
                    minEval = eval;
                }
                board.UndoMove(move);
                /*
                if (eval < beta)
                {
                    beta = eval;
                }
                if (beta <= alpha)
                {
                    break;
                }
                */
            }
            return minEval;
        }
    }


}
public class Benchmark1 : IChessBot
    {
        //                     .  P    K    B    R    Q    K
        int[] kPieceValues = { 0, 100, 300, 310, 500, 900, 10000 };
        int kMassiveNum = 99999999;

        int mDepth;
        Move mBestMove;

        public Move Think(Board board, Timer timer)
        {
            Move[] legalMoves = board.GetLegalMoves();
            mDepth = 3;

            EvaluateBoardNegaMax(board, mDepth, -kMassiveNum, kMassiveNum, board.IsWhiteToMove ? 1 : -1);

            return mBestMove;
        }

        int EvaluateBoardNegaMax(Board board, int depth, int alpha, int beta, int color)
        {
            Move[] legalMoves;

            if (board.IsDraw())
                return 0;

            if (depth == 0 || (legalMoves = board.GetLegalMoves()).Length == 0)
            {
                // EVALUATE
                int sum = 0;

                if (board.IsInCheckmate())
                    return -9999999;

                for (int i = 0; ++i < 7;)
                    sum += (board.GetPieceList((PieceType)i, true).Count - board.GetPieceList((PieceType)i, false).Count) * kPieceValues[i];
                // EVALUATE

                return color * sum;
            }

            // TREE SEARCH
            int recordEval = int.MinValue;
            foreach (Move move in legalMoves)
            {
                board.MakeMove(move);
                int evaluation = -EvaluateBoardNegaMax(board, depth - 1, -beta, -alpha, -color);
                board.UndoMove(move);

                if (recordEval < evaluation)
                {
                    recordEval = evaluation;
                    if (depth == mDepth)
                        mBestMove = move;
                }
                alpha = Math.Max(alpha, recordEval);
                if (alpha >= beta) break;
            }
            // TREE SEARCH

            return recordEval;
        }
    }

public class MyABMinimax : IChessBot
{
    public const int TIME_PER_MOVE = 2500;
    public const int INITIAL_DEPTH = 4;
    public int turns = 0;
    public Move Think(Board board, Timer timer)
    {
        // Move[] moves = board.GetLegalMoves();
        // return moves[0];

        // Move[] legalMoves = board.GetLegalMoves();
        // Move[] captures = board.GetLegalMoves(capturesOnly: true);
        // //randomly shuffle legal moves
        // Random rnd = new Random();
        // legalMoves = legalMoves.OrderBy(x => rnd.Next()).ToArray();
        Move[] captureMoves = board.GetLegalMoves(capturesOnly: true);
        Move[] nonCaptureMoves = board.GetLegalMoves(capturesOnly: false).Except(captureMoves).ToArray();
        //randomly shuffle capture moves and non capture moves
        Random rnd = new Random();
        captureMoves = captureMoves.OrderBy(x => rnd.Next()).ToArray();
        nonCaptureMoves = nonCaptureMoves.OrderBy(x => rnd.Next()).ToArray();
        //create legalMoves array, with captures first then non captures
        Move[] legalMoves = new Move[captureMoves.Length + nonCaptureMoves.Length];
        Array.Copy(captureMoves, legalMoves, captureMoves.Length);
        Array.Copy(nonCaptureMoves, 0, legalMoves, captureMoves.Length, nonCaptureMoves.Length);

        int num_legal_moves = legalMoves.Length;
        //set a random move as best to start
        Move bestMove = legalMoves[0];
        double bestValue = double.NegativeInfinity;
        int boardPieces = getNumPieces(board);
        int depthLeft = INITIAL_DEPTH;
        if (boardPieces <= 16){
            depthLeft++;
        }
        if (boardPieces <= 12) {
            depthLeft++;
        }
        if (boardPieces <= 8) {
            depthLeft++;
        }
        if (boardPieces <= 6) {
            depthLeft++;
        }
        if (boardPieces <= 4) {
            depthLeft++;
        }
        if (timer.MillisecondsRemaining < 30000) {
            depthLeft--;
        }
        if (timer.MillisecondsRemaining < 15000) {
            depthLeft--;
        }
        if (timer.MillisecondsRemaining < 10000) {
            depthLeft--;
        }
        if (timer.MillisecondsRemaining < 5000) {
            depthLeft--;
        }
        int movesChecked = 0;
        bool Reduced = false;
        foreach (Move move in legalMoves) 
            {
                if (timer.MillisecondsElapsedThisTurn > TIME_PER_MOVE && Reduced == false) {
                    depthLeft -= 2;
                    Reduced = true;
                }
                board.MakeMove(move);
                double boardValue = Minimax(board, depthLeft, 0, false, !board.IsWhiteToMove, timer, double.NegativeInfinity, double.PositiveInfinity);
                
                if (boardValue > bestValue) 
                {
                    bestValue = boardValue;
                    bestMove = move;
                }
                board.UndoMove(move);
                movesChecked++;
            }
    turns++;
    return bestMove;
    }
    public int getNumPieces(Board board) {
        int numPieces = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        foreach (PieceList pieceList in pieces) {
            numPieces += pieceList.Count;
        }
        return numPieces;
    }

public double Minimax(Board board, int depthLeft, int depthSoFar, bool isMaximizingPlayer, bool rootIsWhite, Timer timer, double alpha, double beta) 
{
    if (depthLeft == 0 || board.IsInCheckmate() || board.IsDraw())
    {
        return EvaluateBoard(board, rootIsWhite, depthSoFar);
    }

        Move[] captureMoves = board.GetLegalMoves(capturesOnly: true);
        Move[] nonCaptureMoves = board.GetLegalMoves(capturesOnly: false).Except(captureMoves).ToArray();
        //randomly shuffle capture moves and non capture moves
        Random rnd = new Random();
        captureMoves = captureMoves.OrderBy(x => rnd.Next()).ToArray();
        nonCaptureMoves = nonCaptureMoves.OrderBy(x => rnd.Next()).ToArray();
        //create legalMoves array, with captures first then non captures
        Move[] legalMoves = new Move[captureMoves.Length + nonCaptureMoves.Length];
        Array.Copy(captureMoves, legalMoves, captureMoves.Length);
        Array.Copy(nonCaptureMoves, 0, legalMoves, captureMoves.Length, nonCaptureMoves.Length);

    
    if (isMaximizingPlayer) 
    {
        double maxEval = double.NegativeInfinity;
        
        foreach (Move move in legalMoves) 
        {
            board.MakeMove(move);
            double eval = Minimax(board, depthLeft - 1, depthSoFar + 1, false, rootIsWhite, timer, alpha, beta);
            board.UndoMove(move);
            maxEval = Math.Max(maxEval, eval);
            
            // Alpha-beta pruning decision
            alpha = Math.Max(alpha, eval);
            if (beta <= alpha)
            {
                break;
            }
        }
        
        return maxEval;
    } 
    else 
    {
        double minEval = double.PositiveInfinity;
        
        foreach (Move move in legalMoves) 
        {
            board.MakeMove(move);
            double eval = Minimax(board, depthLeft - 1, depthSoFar + 1,  true, rootIsWhite, timer, alpha, beta);
            board.UndoMove(move);
            minEval = Math.Min(minEval, eval);
            
            // Alpha-beta pruning decision
            beta = Math.Min(beta, eval);
            if (beta <= alpha)
            {
                break;
            }
        }
        
        return minEval;
    }
}
    public double EvaluateBoard(Board board, bool rootIsWhite, int depthSoFar) {
        double whiteScore = 0;
        double blackScore = 0;
        PieceList[] pieces = board.GetAllPieceLists();
        if (board.IsDraw()) {
            return -1;
        }
        if (board.IsInCheckmate()) {
            if (board.IsWhiteToMove) {
                whiteScore -= 9999999999 - depthSoFar;
                }
            else {
                blackScore -= 9999999999 - depthSoFar;
                }
            if (rootIsWhite) {
                return whiteScore - blackScore;
                }   
            else {
                return blackScore - whiteScore;
            }
        }
        foreach (PieceList pieceList in pieces) {
            if (pieceList.TypeOfPieceInList == PieceType.Pawn) { //pawn
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 100 * pieceList.Count;
                    // get the pawns square
                    for (int i = 0; i < pieceList.Count; i++) {
                        int rank = pieceList.GetPiece(i).Square.Rank;
                        // if pawn is pushed its worth more
                        whiteScore += (rank >= 3 && rank <= 7) ? 1 << (rank - 1) : 0;
                    }
                }
                else {
                    blackScore += 100 * pieceList.Count;
                    // get the pawns square
                    for (int i = 0; i < pieceList.Count; i++) {
                        int rank = pieceList.GetPiece(i).Square.Rank;
                        // if pawn is pushed its worth more
                        blackScore += (rank >= 2 && rank <= 6) ? 1 << (7 - rank) : 0;
                    }
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Knight) { //knight
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 300 * pieceList.Count;
                }
                else {
                    blackScore += 300 * pieceList.Count;
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Bishop) { //bishop
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 300 * pieceList.Count;
                }
                else {
                    blackScore += 300 * pieceList.Count;
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Rook) { //rook
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 500 * pieceList.Count;
                }
                else {
                    blackScore += 500 * pieceList.Count;
                }
            }
            else if (pieceList.TypeOfPieceInList == PieceType.Queen) { //queen
                if (pieceList.IsWhitePieceList) {
                    whiteScore += 900 * pieceList.Count;
                }
                else {
                    blackScore += 900 * pieceList.Count;
                }
            }
        }
        if (board.IsInCheck()) {
            if (board.IsWhiteToMove) {
                whiteScore -= 200;
            }
            else {
                blackScore -= 200;
            }
        }
        if (rootIsWhite) {
            return whiteScore - blackScore;
        }
        else {
            return blackScore - whiteScore;
        }
    }
}
}

public class Benchmark2 : IChessBot
{
    Move bestmoveRoot = Move.NullMove;

    // https://www.chessprogramming.org/PeSTO%27s_Evaluation_Function
    int[] pieceVal = {0, 100, 310, 330, 500, 1000, 10000 };
    int[] piecePhase = {0, 0, 1, 1, 2, 4, 0};
    ulong[] psts = {657614902731556116, 420894446315227099, 384592972471695068, 312245244820264086, 364876803783607569, 366006824779723922, 366006826859316500, 786039115310605588, 421220596516513823, 366011295806342421, 366006826859316436, 366006896669578452, 162218943720801556, 440575073001255824, 657087419459913430, 402634039558223453, 347425219986941203, 365698755348489557, 311382605788951956, 147850316371514514, 329107007234708689, 402598430990222677, 402611905376114006, 329415149680141460, 257053881053295759, 291134268204721362, 492947507967247313, 367159395376767958, 384021229732455700, 384307098409076181, 402035762391246293, 328847661003244824, 365712019230110867, 366002427738801364, 384307168185238804, 347996828560606484, 329692156834174227, 365439338182165780, 386018218798040211, 456959123538409047, 347157285952386452, 365711880701965780, 365997890021704981, 221896035722130452, 384289231362147538, 384307167128540502, 366006826859320596, 366006826876093716, 366002360093332756, 366006824694793492, 347992428333053139, 457508666683233428, 329723156783776785, 329401687190893908, 366002356855326100, 366288301819245844, 329978030930875600, 420621693221156179, 422042614449657239, 384602117564867863, 419505151144195476, 366274972473194070, 329406075454444949, 275354286769374224, 366855645423297932, 329991151972070674, 311105941360174354, 256772197720318995, 365993560693875923, 258219435335676691, 383730812414424149, 384601907111998612, 401758895947998613, 420612834953622999, 402607438610388375, 329978099633296596, 67159620133902};

    // https://www.chessprogramming.org/Transposition_Table
    struct TTEntry {
        public ulong key;
        public Move move;
        public int depth, score, bound;
        public TTEntry(ulong _key, Move _move, int _depth, int _score, int _bound) {
            key = _key; move = _move; depth = _depth; score = _score; bound = _bound;
        }
    }

    const int entries = (1 << 20);
    TTEntry[] tt = new TTEntry[entries];

    public int getPstVal(int psq) {
        return (int)(((psts[psq / 10] >> (6 * (psq % 10))) & 63) - 20) * 8;
    }

    public int Evaluate(Board board) {
        int mg = 0, eg = 0, phase = 0;

        foreach(bool stm in new[] {true, false}) {
            for(var p = PieceType.Pawn; p <= PieceType.King; p++) {
                int piece = (int)p, ind;
                ulong mask = board.GetPieceBitboard(p, stm);
                while(mask != 0) {
                    phase += piecePhase[piece];
                    ind = 128 * (piece - 1) + BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^ (stm ? 56 : 0);
                    mg += getPstVal(ind) + pieceVal[piece];
                    eg += getPstVal(ind + 64) + pieceVal[piece];
                }
            }

            mg = -mg;
            eg = -eg;
        }

        return (mg * phase + eg * (24 - phase)) / 24 * (board.IsWhiteToMove ? 1 : -1);
    }

    // https://www.chessprogramming.org/Negamax
    // https://www.chessprogramming.org/Quiescence_Search
    public int Search(Board board, Timer timer, int alpha, int beta, int depth, int ply) {
        ulong key = board.ZobristKey;
        bool qsearch = depth <= 0;
        bool notRoot = ply > 0;
        int best = -30000;

        // Check for repetition (this is much more important than material and 50 move rule draws)
        if(notRoot && board.IsRepeatedPosition())
            return 0;

        TTEntry entry = tt[key % entries];

        // TT cutoffs
        if(notRoot && entry.key == key && entry.depth >= depth && (
            entry.bound == 3 // exact score
                || entry.bound == 2 && entry.score >= beta // lower bound, fail high
                || entry.bound == 1 && entry.score <= alpha // upper bound, fail low
        )) return entry.score;

        int eval = Evaluate(board);

        // Quiescence search is in the same function as negamax to save tokens
        if(qsearch) {
            best = eval;
            if(best >= beta) return best;
            alpha = Math.Max(alpha, best);
        }

        // Generate moves, only captures in qsearch
        Move[] moves = board.GetLegalMoves(qsearch);
        int[] scores = new int[moves.Length];

        // Score moves
        for(int i = 0; i < moves.Length; i++) {
            Move move = moves[i];
            // TT move
            if(move == entry.move) scores[i] = 1000000;
            // https://www.chessprogramming.org/MVV-LVA
            else if(move.IsCapture) scores[i] = 100 * (int)move.CapturePieceType - (int)move.MovePieceType;
        }

        Move bestMove = Move.NullMove;
        int origAlpha = alpha;

        // Search moves
        for(int i = 0; i < moves.Length; i++) {
            if(timer.MillisecondsElapsedThisTurn >= timer.MillisecondsRemaining / 30) return 30000;

            // Incrementally sort moves
            for(int j = i + 1; j < moves.Length; j++) {
                if(scores[j] > scores[i])
                    (scores[i], scores[j], moves[i], moves[j]) = (scores[j], scores[i], moves[j], moves[i]);
            }

            Move move = moves[i];
            board.MakeMove(move);
            int score = -Search(board, timer, -beta, -alpha, depth - 1, ply + 1);
            board.UndoMove(move);

            // New best move
            if(score > best) {
                best = score;
                bestMove = move;
                if(ply == 0) bestmoveRoot = move;

                // Improve alpha
                alpha = Math.Max(alpha, score);

                // Fail-high
                if(alpha >= beta) break;

            }
        }

        // (Check/Stale)mate
        if(!qsearch && moves.Length == 0) return board.IsInCheck() ? -30000 + ply : 0;

        // Did we fail high/low or get an exact score?
        int bound = best >= beta ? 2 : best > origAlpha ? 3 : 1;

        // Push to TT
        tt[key % entries] = new TTEntry(key, bestMove, depth, best, bound);

        return best;
    }

    public Move Think(Board board, Timer timer)
    {
        bestmoveRoot = Move.NullMove;
        // https://www.chessprogramming.org/Iterative_Deepening
        for(int depth = 1; depth <= 50; depth++) {
            int score = Search(board, timer, -30000, 30000, depth, 0);

            // Out of time
            if(timer.MillisecondsElapsedThisTurn >= timer.MillisecondsRemaining / 30)
                break;
        }
        return bestmoveRoot.IsNull ? board.GetLegalMoves()[0] : bestmoveRoot;
    }
}