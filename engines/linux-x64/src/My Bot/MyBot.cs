﻿using ChessChallenge.API;
using System;

public class MyBot : IChessBot
{
    // { None, Pawn, Knight, Bishop, Rook, Queen, King}
    private int[] _pieceValues = { 0, 100, 300, 320, 500, 950, 10000 };
    private int[] _bonusPointsPerAttackEarly = { 0, 0, 4, 5, 1, 1, 0 };
    private int[] _bonusPointsPerAttackLate = { 0, 0, 2, 3, 5, 3, 1 };
    private int[] _moveScores = new int[218]; // for sorting moves
    Random _rng = new Random();

    // Transposition table
    private TTEntry[] _ttEntries = new TTEntry[16000000];
    int count = 0;


    public Move Think(Board board, Timer timer)
    {
        count++;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("Move Number: ");
        Console.WriteLine(board.PlyCount / 2 + 1);
        Console.ResetColor();

        Span<Move> legalMoves = stackalloc Move[218];
        board.GetLegalMovesNonAlloc(ref legalMoves);
        Move bestMove = legalMoves[0];

        // Forced move, don't waste time searching
        if (legalMoves.Length == 1) {
            Console.WriteLine("MyBot: Forced Move");
            return bestMove;
        }

        if(board.PlyCount == 0 && board.IsWhiteToMove) {
            Console.WriteLine("MyBot: Opening Move");
            return legalMoves[16];
        }
        
        // push pawn on non attacked center square
        if(board.PlyCount == 1 && !board.IsWhiteToMove) {
            Console.WriteLine("MyBot: Opening Move");
            if (board.SquareIsAttackedByOpponent(legalMoves[15].TargetSquare)) {
                return legalMoves[16];                
            }
            else
                return legalMoves[15];
        }

        int currentDepth;
        int rootEval = 0;

        if(timer.MillisecondsRemaining <= 10000){
            for (currentDepth = 1; 
                timer.MillisecondsElapsedThisTurn < Math.Min(100, timer.MillisecondsRemaining / 60); 
                currentDepth++)
            {
                rootEval = Search(currentDepth, 0, -1000000000, 1000000000);
            }
        }

        else {        
            for (currentDepth = 1; 
                timer.MillisecondsElapsedThisTurn < Math.Min(board.PlyCount <= 14 ? 500 : timer.GameStartTimeMilliseconds / 60, (timer.MillisecondsRemaining - 10000) / 60); 
                currentDepth++)
            {
                rootEval = Search(currentDepth, 0, -1000000000, 1000000000);
            }
        }

        Console.Write("MyBot: ");
        Console.WriteLine(currentDepth);

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("Eval -> ");
        Console.WriteLine(rootEval/100.0);
        Console.ResetColor();

        
        return bestMove;
        
        int Search(int depth, int ply, int alpha, int beta)
        {
            // First check if there's a checkmate
            if (board.IsInCheckmate())
                return -100000 + ply * 1000; // multiply by depth, the sooner the mate the better

            if (board.IsDraw())
                return 0;

            // Try get evaluation from Transposition Table
            TTEntry entry = _ttEntries[board.ZobristKey % 16000000];
            int tableEval = entry._evalValue;

            if (ply != 0 && entry._zobristKey == board.ZobristKey && entry._depth >= depth
                && (entry._nodeType == 0
                || entry._nodeType == 1 && tableEval <= alpha
                || entry._nodeType == 2 && tableEval >= beta))
                return tableEval;

            int extend = 0;
            if (board.IsInCheck()) // Check extension
                extend = 1;

            if (depth <= 2)
                return QSearch(alpha, beta);

            byte evalType = 1; // Alpha
            Span<Move> legalMoves = stackalloc Move[218];
            board.GetLegalMovesNonAlloc(ref legalMoves);

            if (legalMoves.Length > 1)
                OrderMoves(ref legalMoves, ply == 0 && depth > 1);
            else
                extend = 1; // Forced move/One reply extension

            foreach (Move move in legalMoves)
            {
                board.MakeMove(move);
                int eval = -Search(depth - 1 + extend, ply + 1, -beta, -alpha);
                board.UndoMove(move);

                if (eval >= beta)
                {
                    StoreEvalInTT(eval, 2); // Beta
                    return beta;
                }

                if (eval > alpha)
                {
                    evalType = 0; // Exact
                    alpha = eval;
                    if (ply == 0)
                        bestMove = move;
                }
            }

            StoreEvalInTT(alpha, evalType);
            return alpha;

            void StoreEvalInTT(int evalValue, byte nodeType)
            {
                _ttEntries[board.ZobristKey % 16000000] = new TTEntry
                {
                    _nodeType = nodeType,
                    _depth = (byte)depth,
                    _zobristKey = board.ZobristKey,
                    _evalValue = evalValue
                };
            }
        }

        // Search only captures
        int QSearch(int alpha, int beta)
        {
            int eval = Evaluate() * (board.IsWhiteToMove ? 1 : -1);
            if (eval >= beta) return beta;
            if (alpha < eval) alpha = eval;

            Span<Move> legalMoves = stackalloc Move[256];
            board.GetLegalMovesNonAlloc(ref legalMoves, true);
            OrderMoves(ref legalMoves, false);

            foreach (Move move in legalMoves)
            {
                board.MakeMove(move);
                eval = -QSearch(-beta, -alpha);
                board.UndoMove(move);

                if (eval >= beta) return beta;
                if (eval > alpha) alpha = eval;
            }

            return alpha;
        }

        // Evaluates a board, positive score is good for white, negative for black
        int Evaluate()
        {
            // Evaluate based on material value
            int evaluation = 0;

            // Calculate total pieces and queen presence
            int totalPieces = BitboardHelper.GetNumberOfSetBits(board.AllPiecesBitboard);

            bool noQueens = board.GetPieceList(PieceType.Queen, true).Count == 0 
                            && board.GetPieceList(PieceType.Queen, false).Count == 0;

            bool isEndgame = (noQueens && totalPieces <= 16) || totalPieces <= 10;

            var bonusPointsPerAttack = isEndgame || totalPieces <= 12 ? _bonusPointsPerAttackLate : _bonusPointsPerAttackEarly;

            foreach (PieceType pieceType in Enum.GetValues(typeof(PieceType)))
            {
                if (pieceType == PieceType.None)
                    continue;

                EvaluatePieces(pieceType, true, 1);
                EvaluatePieces(pieceType, false, -1);
            }

            if (board.PlyCount >= 16 && board.PlyCount <= 30) evaluation += EvaluateCastlingBonus(board);

            if(evaluation >= 0 && totalPieces <= 7) {
                evaluation += MopUp();
            }

            // Add a tiny bit of rng to eval, this way we can pick evaluated positions with same score
            return evaluation + _rng.Next(-1, 2);

            void EvaluatePieces(PieceType pieceType, bool isWhite, int sign)
            {
                var pieceList = board.GetPieceList(pieceType, isWhite);
                evaluation += pieceList.Count * _pieceValues[(int)pieceType] * sign; // Evaluate material value

                // Evaluate attacks
                foreach (var piece in pieceList)
                {
                    var pieceBitboard = BitboardHelper.GetPieceAttacks(pieceType, piece.Square, board, isWhite);
                    var attacks = BitboardHelper.GetNumberOfSetBits(pieceBitboard);
                    evaluation += attacks * bonusPointsPerAttack[(int)pieceType] * sign;
                }

                // Evaluate Pawns
                if (pieceType == PieceType.Pawn)
                {
                    var pawnFileFlags = 0;

                    foreach (var pawn in pieceList)
                    {
                        var fileFlag = 1 << pawn.Square.File;

                        // Double pawn penalty
                        if ((pawnFileFlags & fileFlag) != 0) // We know there was a pawn on this file, so it's a double pawn
                            evaluation -= 15 * sign;

                        pawnFileFlags |= fileFlag;

                        // Passed pawns
                        ulong passedPawnMask = 0;
                        BitboardHelper.SetSquare(ref passedPawnMask, pawn.Square);

                        if (pawn.Square.File < 7)
                            passedPawnMask |= passedPawnMask << 1;

                        if (pawn.Square.File > 0)
                            passedPawnMask |= passedPawnMask >> 1;

                        if (isWhite)
                        {
                            passedPawnMask <<= 8;
                            passedPawnMask |= passedPawnMask << 8;
                            passedPawnMask |= passedPawnMask << 16;
                            passedPawnMask |= passedPawnMask << 32;
                        }
                        else
                        {
                            passedPawnMask >>= 8;
                            passedPawnMask |= passedPawnMask >> 8;
                            passedPawnMask |= passedPawnMask >> 16;
                            passedPawnMask |= passedPawnMask >> 32;
                        }

                        // Passed pawn bonus, the closer to promotion the better
                        if ((passedPawnMask & board.GetPieceBitboard(PieceType.Pawn, !isWhite)) == 0) // Check intersection between mask and enemy pawns
                            evaluation += 15 * (isWhite ? pawn.Square.Rank : 7 - pawn.Square.Rank) * sign;
                    }

                    foreach (var pawn in pieceList)
                    {
                        var fileFlag = 1 << pawn.Square.File;

                        // Isolated pawn penalty
                        if ((pawnFileFlags & ((fileFlag << 1) | (fileFlag >> 1))) == 0) // Check adjacent files for other friendly pawns
                            evaluation -= 15 * sign;
                    }
                }

            }
            
            int EvaluateCastlingBonus(Board board)
            {
                int bonus = 0;
                
                Square whiteKingSquare = board.GetKingSquare(board.IsWhiteToMove);
                if (whiteKingSquare.File >= 6 && whiteKingSquare.Rank == 0) // Kingside (g1)
                    bonus += 150;
                else if (whiteKingSquare.File <= 2 && whiteKingSquare.Rank == 0) // Queenside (c1)
                    bonus += 150;
    
                Square blackKingSquare = board.GetKingSquare(!board.IsWhiteToMove);
                if (blackKingSquare.File >= 6 && blackKingSquare.Rank == 7) // Kingside (g8)
                    bonus -= 150;
                else if (blackKingSquare.File <= 2 && blackKingSquare.Rank == 7) // Queenside (c8)
                    bonus -= 150;

                return bonus;
            }

            int MopUp() {
                    Square goodKing = board.GetKingSquare(board.IsWhiteToMove);
                    Square evilKing = board.GetKingSquare(!board.IsWhiteToMove);

                    int distance = Math.Abs(goodKing.File - evilKing.File) + Math.Abs(goodKing.Rank - evilKing.Rank);

                    int mopUpBonus = 0;

                    mopUpBonus += (14 - distance) * 5;

                    mopUpBonus += distanceFromCenter(evilKing);
                    
                    return mopUpBonus;
                }

                int distanceFromCenter (Square square) {
                    return Math.Max(3 - square.File, square.File - 4) + Math.Max(3 - square.Rank, square.Rank - 3);
                }
        }

        void OrderMoves(ref Span<Move> moves, bool useBestMove)
        {
            for (int i = 0; i < moves.Length; i++)
            {
                Move move = moves[i];
                _moveScores[i] = 0;

                if (useBestMove && move == bestMove)
                {
                    _moveScores[i] = 10000000;
                    continue;
                }

                if (move.CapturePieceType != PieceType.None)
                {
                    int captureValue = _pieceValues[(int)move.CapturePieceType] - _pieceValues[(int)move.MovePieceType];
                    _moveScores[i] += 10 * captureValue;
                }

                if (move.IsPromotion)
                    _moveScores[i] += _pieceValues[(int)move.PromotionPieceType];

                if (move.IsCastles)
                    _moveScores[i] += 10000;

                if (board.SquareIsAttackedByOpponent(move.TargetSquare))
                    _moveScores[i] -= _pieceValues[(int)move.MovePieceType];
            }

            _moveScores.AsSpan().Slice(0, moves.Length).Sort(moves, (a, b) => b.CompareTo(a));
        }
    }

    public struct TTEntry
    {
        public int _evalValue;
        public byte _depth;
        public byte _nodeType; // 0 = Exact, 1 = Alpha, 2 = Beta
        public ulong _zobristKey;
    }
}