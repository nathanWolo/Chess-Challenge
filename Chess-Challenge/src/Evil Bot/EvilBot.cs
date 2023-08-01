using ChessChallenge.API;
using System;

namespace ChessChallenge.Example
{

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