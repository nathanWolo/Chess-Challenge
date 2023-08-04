using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

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
            Console.Write("best move: {0}, value: {1}, depth: {2}, positions evaluated: {3}, in {4} ms\n", 
                bestMoveTemp, maxEval, depthLeft, positionsEvaluated,timer.MillisecondsElapsedThisTurn );
            if (timer.MillisecondsElapsedThisTurn >= TIME_PER_MOVE) {https://opengraph.githubassets.com/cd79a4364c5d81d7378baa14ad99a4f19a83866a4192908ecb52a65085cb0c1e/GheorgheMorari/Chess-Challenge
                break;
            }
            //aspiration window
            if ((maxEval <= alpha || maxEval >= beta) && maxEval > -999999999 && maxEval < 999999999) { //fail low or high, ignore out of checkmate bounds and draws
                Console.WriteLine("Search failed due to narrow aspiration window, doubling window and trying again");
                alpha = alpha - aspiration;
                beta = beta + aspiration;
                aspiration *= 2;
            }
            else {
                alpha = maxEval - aspiration;
                beta = maxEval + aspiration;
                Console.WriteLine("Aspiration window: [{0}, {1}]", alpha, beta);
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