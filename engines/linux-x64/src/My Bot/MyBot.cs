﻿using ChessChallenge.API;
using System;

public class MyBot : IChessBot
{
    private const int MAX_PLY = 256;
    private const int KILLER_MOVE_BONUS = 9000;
    private const int COUNTERMOVE_BONUS = 8000;
    private const int IID_THRESHOLD = 3;

    // Evaluation constants
    private const int DOUBLE_PAWN_PENALTY = 15;
    private const int PASSED_PAWN_BONUS = 15;
    private const int ISOLATED_PAWN_PENALTY = 15;
    private const int CASTLE_BONUS = 50;
    private const int MOP_UP_MAX_DISTANCE = 14;
    private const int MOP_UP_DISTANCE_BONUS_MULTIPLIER = 5;
    private const int CENTER_FILE_THRESHOLD_LOW = 3;
    private const int CENTER_FILE_THRESHOLD_HIGH = 4;
    private const int CENTER_RANK_THRESHOLD_LOW = 3;
    private const int CENTER_RANK_THRESHOLD_HIGH = 3;

    // Move ordering constants
    private const int BEST_MOVE_SCORE = 10000000;
    private const int CAPTURE_SCORE_BASE = 100000;
    private const int PROMOTION_SCORE_MULTIPLIER = 1; // Using piece value directly
    private const int CASTLE_SCORE = 10000;
    private const int KING_PROTECTION_BONUS_DIVISOR = 500000;

    // Piece‑Square tables for white
    private static readonly int[] PawnTable = new int[64] {
        0,   0,   0,   0,   0,   0,   0,   0,
        50,  50,  50,  50,  50,  50,  50,  50,
        10,  10,  20,  30,  30,  20,  10,  10,
        5,   5,  10,  25,  25,  10,   5,   5,
        0,   0,   0,  20,  20,   0,   0,   0,
        5,  -5, -10,   0,   0, -10,  -5,   5,
        5,  10,  10, -20, -20,  10,  10,   5,
        0,   0,   0,   0,   0,   0,   0,   0
    };

    private static readonly int[] KnightTable = new int[64] {
        -50, -40, -30, -30, -30, -30, -40, -50,
        -40, -20,   0,   0,   0,   0, -20, -40,
        -30,   0,  10,  15,  15,  10,   0, -30,
        -30,   5,  15,  20,  20,  15,   5, -30,
        -30,   0,  15,  20,  20,  15,   0, -30,
        -30,   5,  10,  15,  15,  10,   5, -30,
        -40, -20,   0,   5,   5,   0, -20, -40,
        -50, -40, -30, -30, -30, -30, -40, -50
    };

    private static readonly int[] BishopTable = new int[64] {
        -20, -10, -10, -10, -10, -10, -10, -20,
        -10,   0,   0,   0,   0,   0,   0, -10,
        -10,   0,   5,  10,  10,   5,   0, -10,
        -10,   5,   5,  10,  10,   5,   5, -10,
        -10,   0,  10,  10,  10,  10,   0, -10,
        -10,  10,  10,  10,  10,  10,  10, -10,
        -10,   5,   0,   0,   0,   0,   5, -10,
        -20, -10, -10, -10, -10, -10, -10, -20
    };

    private static readonly int[] RookTable = new int[64] {
        0,   0,   0,   0,   0,   0,   0,   0,
        5,  10,  10,  10,  10,  10,  10,   5,
        -5,   0,   0,   0,   0,   0,   0,  -5,
        -5,   0,   0,   0,   0,   0,   0,  -5,
        -5,   0,   0,   0,   0,   0,   0,  -5,
        -5,   0,   0,   0,   0,   0,   0,  -5,
        -5,   0,   0,   0,   0,   0,   0,  -5,
        0,   0,   0,   5,   5,   0,   0,   0
    };

    private static readonly int[] QueenTable = new int[64] {
        -20, -10, -10,  -5,  -5, -10, -10, -20,
        -10,   0,   0,   0,   0,   0,   0, -10,
        -10,   0,   5,   5,   5,   5,   0, -10,
        -5,   0,   5,   5,   5,   5,   0,  -5,
        0,   0,   5,   5,   5,   5,   0,  -5,
        -10,   5,   5,   5,   5,   5,   0, -10,
        -10,   0,   5,   0,   0,   0,   0, -10,
        -20, -10, -10,  -5,  -5, -10, -10, -20
    };

    private static readonly int[] KingTable = new int[64] {
        -30, -40, -40, -50, -50, -40, -40, -30,
        -30, -40, -40, -50, -50, -40, -40, -30,
        -30, -40, -40, -50, -50, -40, -40, -30,
        -30, -40, -40, -50, -50, -40, -40, -30,
        -20, -30, -30, -40, -40, -30, -30, -20,
        -10, -20, -20, -20, -20, -20, -20, -10,
        20,  20,   0,   0,   0,   0,  20,  20,
        20,  30,  10,   0,   0,  10,  30,  20
    };

    // { None, Pawn, Knight, Bishop, Rook, Queen, King}
    private int[] _pieceValues = { 0, 100, 300, 320, 500, 1000, 10000 };
    private int[] _bonusPointsPerAttackEarly = { 0, 0, 4, 5, 1, 1, 0 };
    private int[] _bonusPointsPerAttackLate = { 0, 0, 2, 3, 5, 3, 1 };
    private int[] _moveScores = new int[218]; // for sorting moves
    private int[,] history = new int[7, 64];
    private Move[,] countermoveTable = new Move[64, 64];  // Indexed by [fromSquare, toSquare]
    private Move[,] killerMoves = new Move[MAX_PLY, 2];

    Random _rng = new Random();

    // Transposition table
    private TTEntry[] _ttEntries = new TTEntry[16000000];
    int count = 0;
    private Board _board;
    private Move _bestMove;
    private int _nodes;

    // Mirrors a square index vertically (for black).
    private int MirrorIndex(int index)
    {
        int rank = index / 8;
        int file = index % 8;
        return (7 - rank) * 8 + file;
    }

    // Returns the piece-square bonus for a given piece type at a target square.
    private int GetPieceSquareBonus(PieceType pieceType, Square targetSquare, bool isWhite)
    {
        int index = targetSquare.Index;
        if (!isWhite)
            index = MirrorIndex(index);
        switch (pieceType)
        {
            case PieceType.Pawn:
                return PawnTable[index];
            case PieceType.Knight:
                return KnightTable[index];
            case PieceType.Bishop:
                return BishopTable[index];
            case PieceType.Rook:
                return RookTable[index];
            case PieceType.Queen:
                return QueenTable[index];
            case PieceType.King:
                return KingTable[index];
            default:
                return 0;
        }
    }

    public Move Think(Board board, Timer timer)
    {
        _nodes = 0;
        count++;
        _board = board;
        // Console.ForegroundColor = ConsoleColor.Green;
        // Console.Write("Move Number: ");
        // Console.WriteLine(board.PlyCount / 2 + 1);
        // Console.ResetColor();

        Span<Move> legalMoves = stackalloc Move[218];
        board.GetLegalMovesNonAlloc(ref legalMoves);
        _bestMove = legalMoves[0];

        // Forced move, don't waste time searching
        if (legalMoves.Length == 1)
        {
            // Console.WriteLine("MyBot: Forced Move");
            return _bestMove;
        }

        if (board.PlyCount == 0 && board.IsWhiteToMove)
        {
            // Console.WriteLine("MyBot: Opening Move");
            return legalMoves[16];
        }

        // push pawn on non attacked center square
        if (board.PlyCount == 1 && !board.IsWhiteToMove)
        {
            // Console.WriteLine("MyBot: Opening Move");
            if (board.SquareIsAttackedByOpponent(legalMoves[15].TargetSquare))
            {
                return legalMoves[16];
            }
            else
                return legalMoves[15];
        }

        int currentDepth = 1;
        int rootEval = 0;

        // allocate one-quarter of the remaining time for searching
        int maxSearchTime = timer.MillisecondsRemaining / 4;

        try
        {
            // continue deepening until reaching max depth or using too much of the allocated time
            while (currentDepth <= 200 && timer.MillisecondsElapsedThisTurn < maxSearchTime / 10)
            {
                rootEval = Search(currentDepth, 0, -1000000000, 1000000000);
                // Console.WriteLine($"info depth {currentDepth} nodes {nodes} score cp {rootEval / 100.0} time {timer.MillisecondsElapsedThisTurn} pv {ChessChallenge.Chess.MoveUtility.GetMoveNameUCI(new(bestMove.RawValue))}");
                Console.WriteLine(
                    "info depth {0} time {1} nodes {2} pv {3} score cp {4}",
                    currentDepth,
                    timer.MillisecondsElapsedThisTurn,
                    _nodes,
                    ChessChallenge.Chess.MoveUtility.GetMoveNameUCI(new(_bestMove.RawValue)),
                    rootEval
                );
                currentDepth++;
            }
        }
        catch (Exception)
        {
            // exit gracefully on timeout...
        }

        // Console.Write("MyBot: ");
        // Console.WriteLine(currentDepth - 1);

        // Console.ForegroundColor = ConsoleColor.Yellow;
        // Console.Write("Eval -> ");
        // Console.WriteLine(rootEval/100.0);
        // Console.ResetColor();

        return _bestMove;
    }

    private int Search(int depth, int ply, int alpha, int beta, Move prevMove = default)
    {
        _nodes++;
        // First check if there's a checkmate
        if (_board.IsInCheckmate())
            return -100000 + ply * 1000; // multiply by depth, the sooner the mate the better

        if (_board.IsDraw())
            return 0;

        // Try get evaluation from Transposition Table
        TTEntry entry = _ttEntries[_board.ZobristKey % 16000000];
        int tableEval = entry._evalValue;

        if (depth >= IID_THRESHOLD && entry._zobristKey != _board.ZobristKey)
        {
            int iidEval = Search(depth / 2, ply, alpha, beta);
        }

        if (ply != 0 && entry._zobristKey == _board.ZobristKey && entry._depth >= depth
            && (entry._nodeType == 0
            || entry._nodeType == 1 && tableEval <= alpha
            || entry._nodeType == 2 && tableEval >= beta))
            return tableEval;

        int extend = 0;
        if (_board.IsInCheck()) // Check extension
            extend = 1;

        if (depth == 0)
            return QSearch(alpha, beta);

        int totalPieces = BitboardHelper.GetNumberOfSetBits(_board.AllPiecesBitboard);
        if (depth >= 2 && !_board.IsInCheck() && totalPieces > 8)
        {
            if (_board.TrySkipTurn())
            {
                int nullReduction = 2;
                int nullScore = -Search(depth - nullReduction, ply + 1, -beta, -beta + 1);
                _board.UndoSkipTurn();
                if (nullScore >= beta)
                {
                    StoreEvalInTT(nullScore, 2, depth);
                    return beta;
                }
            }
        }

        byte evalType = 1; // Alpha
        Span<Move> legalMoves = stackalloc Move[218];
        _board.GetLegalMovesNonAlloc(ref legalMoves);

        if (legalMoves.Length > 1)
            OrderMoves(ref legalMoves, ply == 0 && depth > 1, ply, prevMove);
        else
            extend = 1; // Forced move/One reply extension

        int moveIndex = 0;
        foreach (Move move in legalMoves)
        {
            _board.MakeMove(move);
            int eval;
            // reduction for later moves if depth is sufficient
            if (moveIndex > 1 && depth > 1)
            {
                // search at a reduced depth (reduced by one ply)
                eval = -Search(depth - 2 + extend, ply + 1, -beta, -alpha, move);
                // if the reduced search shows promise, re-search at full depth
                if (eval > alpha)
                {
                    eval = -Search(depth - 1 + extend, ply + 1, -beta, -alpha, move);
                }
            }
            else
            {
                eval = -Search(depth - 1 + extend, ply + 1, -beta, -alpha);
            }

            _board.UndoMove(move);

            if (eval >= beta)
            {
                if (ply > 0 && prevMove.RawValue != 0)
                {
                    countermoveTable[prevMove.StartSquare.Index, prevMove.TargetSquare.Index] = move;
                }
                if (move.CapturePieceType == PieceType.None && !move.IsPromotion)
                {
                    if (killerMoves[ply, 0].RawValue != move.RawValue)
                    {
                        killerMoves[ply, 1] = killerMoves[ply, 0];
                        killerMoves[ply, 0] = move;
                    }
                }
                int bonus = depth * depth;
                history[(int)move.MovePieceType, move.TargetSquare.Index] += bonus;
                StoreEvalInTT(eval, 2, depth);
                return beta; // Cut off the branch
            }

            if (eval > alpha)
            {
                evalType = 0; // Exact
                alpha = eval;
                if (ply == 0)
                    _bestMove = move;
            }
            moveIndex++;
        }

        StoreEvalInTT(alpha, evalType, depth);
        return alpha;
    }

    // Search only captures
    private int QSearch(int alpha, int beta)
    {
        int eval = Evaluate() * (_board.IsWhiteToMove ? 1 : -1);
        if (eval >= beta) return beta;
        if (alpha < eval) alpha = eval;

        Span<Move> legalMoves = stackalloc Move[256];
        _board.GetLegalMovesNonAlloc(ref legalMoves, true);
        OrderMoves(ref legalMoves, false);

        foreach (Move move in legalMoves)
        {
            _board.MakeMove(move);
            eval = -QSearch(-beta, -alpha);
            _board.UndoMove(move);

            if (eval >= beta) return beta;
            if (eval > alpha) alpha = eval;
        }

        return alpha;
    }

    // Evaluates a board, positive score is good for white, negative for black
    private int Evaluate()
    {
        // Evaluate based on material value
        int evaluation = 0;

        // Calculate total pieces and queen presence
        int totalPieces = BitboardHelper.GetNumberOfSetBits(_board.AllPiecesBitboard);

        bool noQueens = _board.GetPieceList(PieceType.Queen, true).Count == 0
                        && _board.GetPieceList(PieceType.Queen, false).Count == 0;

        bool isEndgame = (noQueens && totalPieces <= 16) || totalPieces <= 10;

        var bonusPointsPerAttack = isEndgame || totalPieces <= 12 ? _bonusPointsPerAttackLate : _bonusPointsPerAttackEarly;

        foreach (PieceType pieceType in Enum.GetValues(typeof(PieceType)))
        {
            if (pieceType == PieceType.None)
                continue;

            EvaluatePieces(pieceType, true, 1);
            EvaluatePieces(pieceType, false, -1);
        }

        if (_board.PlyCount >= 16 && _board.PlyCount <= 30) evaluation += EvaluateCastlingBonus(_board);

        if (evaluation >= 0 && totalPieces <= 7)
        {
            evaluation += MopUp();
        }

        // Add a tiny bit of rng to eval, this way we can pick evaluated positions with same score
        return evaluation + _rng.Next(-1, 2);

        void EvaluatePieces(PieceType pieceType, bool isWhite, int sign)
        {
            var pieceList = _board.GetPieceList(pieceType, isWhite);
            evaluation += pieceList.Count * _pieceValues[(int)pieceType] * sign; // Evaluate material value

            // Evaluate attacks
            foreach (var piece in pieceList)
            {
                var pieceBitboard = BitboardHelper.GetPieceAttacks(pieceType, piece.Square, _board, isWhite);
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
                        evaluation -= DOUBLE_PAWN_PENALTY * sign;

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
                    if ((passedPawnMask & _board.GetPieceBitboard(PieceType.Pawn, !isWhite)) == 0) // Check intersection between mask and enemy pawns
                        evaluation += PASSED_PAWN_BONUS * (isWhite ? pawn.Square.Rank : 7 - pawn.Square.Rank) * sign;
                }

                foreach (var pawn in pieceList)
                {
                    var fileFlag = 1 << pawn.Square.File;

                    // Isolated pawn penalty
                    if ((pawnFileFlags & ((fileFlag << 1) | (fileFlag >> 1))) == 0) // Check adjacent files for other friendly pawns
                        evaluation -= ISOLATED_PAWN_PENALTY * sign;
                }
            }
        }

        int EvaluateCastlingBonus(Board board)
        {
            int bonus = 0;

            Square whiteKing = board.GetKingSquare(true);
            if ((whiteKing.File == 6 && whiteKing.Rank == 0) || (whiteKing.File == 2 && whiteKing.Rank == 0))
            {
                bonus += CASTLE_BONUS;
            }

            Square blackKing = board.GetKingSquare(false);
            if ((blackKing.File == 6 && blackKing.Rank == 7) || (blackKing.File == 2 && blackKing.Rank == 7))
            {
                bonus += -CASTLE_BONUS;
            }

            return bonus;
        }

        int MopUp()
        {
            Square goodKing = _board.GetKingSquare(_board.IsWhiteToMove);
            Square evilKing = _board.GetKingSquare(!_board.IsWhiteToMove);

            int distance = Math.Abs(goodKing.File - evilKing.File) + Math.Abs(goodKing.Rank - evilKing.Rank);

            int mopUpBonus = 0;

            mopUpBonus += (MOP_UP_MAX_DISTANCE - distance) * MOP_UP_DISTANCE_BONUS_MULTIPLIER;

            mopUpBonus += distanceFromCenter(evilKing);

            return mopUpBonus;
        }

        int distanceFromCenter(Square square)
        {
            return Math.Max(CENTER_FILE_THRESHOLD_LOW - square.File, square.File - CENTER_FILE_THRESHOLD_HIGH) + Math.Max(CENTER_RANK_THRESHOLD_LOW - square.Rank, square.Rank - CENTER_RANK_THRESHOLD_HIGH);
        }
    }

    private bool IsProtectingKing(Move move, Board board)
    {
        if (move.MovePieceType == PieceType.King)
            return false;

        Square kingSquare = board.GetKingSquare(board.IsWhiteToMove);

        int fileDiff = Math.Abs(move.TargetSquare.File - kingSquare.File);
        int rankDiff = Math.Abs(move.TargetSquare.Rank - kingSquare.Rank);

        return fileDiff <= 2 && rankDiff <= 2;
    }

    private void OrderMoves(ref Span<Move> moves, bool useBestMove, int ply = -1, Move prevMove = default)
    {
        for (int i = 0; i < moves.Length; i++)
        {
            Move move = moves[i];
            _moveScores[i] = 0;

            if (useBestMove && move == _bestMove)
            {
                _moveScores[i] = BEST_MOVE_SCORE;
                continue;
            }

            if (move.CapturePieceType != PieceType.None)
            {
                _moveScores[i] += CAPTURE_SCORE_BASE + (_pieceValues[(int)move.CapturePieceType] - _pieceValues[(int)move.MovePieceType]);
            }
            else
            {
                _moveScores[i] += history[(int)move.MovePieceType, move.TargetSquare.Index];

                // Add bonus from countermove heuristic, if applicable.
                if (countermoveTable[prevMove.StartSquare.Index, prevMove.TargetSquare.Index].RawValue == move.RawValue)
                {
                    _moveScores[i] += COUNTERMOVE_BONUS;
                }
                // Add bonus from killer moves if applicable.
                if (ply != -1 && move.CapturePieceType == PieceType.None && !move.IsPromotion)
                {
                    if (killerMoves[ply, 0].RawValue == move.RawValue || killerMoves[ply, 1].RawValue == move.RawValue)
                    {
                        _moveScores[i] += KILLER_MOVE_BONUS;
                    }
                }
                // Add piece-square table bonus for quiet moves.
                int totalPieces = BitboardHelper.GetNumberOfSetBits(_board.AllPiecesBitboard);
                if (totalPieces > 12)
                {
                    _moveScores[i] += GetPieceSquareBonus(move.MovePieceType, move.TargetSquare, _board.IsWhiteToMove);

                    if (IsProtectingKing(move, _board))
                        _moveScores[i] += KING_PROTECTION_BONUS_DIVISOR / _pieceValues[(int)move.MovePieceType];
                }
            }

            if (move.IsPromotion)
                _moveScores[i] += _pieceValues[(int)move.PromotionPieceType] * PROMOTION_SCORE_MULTIPLIER;

            if (move.IsCastles)
                _moveScores[i] += CASTLE_SCORE;

            if (_board.SquareIsAttackedByOpponent(move.TargetSquare))
                _moveScores[i] -= _pieceValues[(int)move.MovePieceType];
        }

        _moveScores.AsSpan().Slice(0, moves.Length).Sort(moves, (a, b) => b.CompareTo(a));
    }

    private void StoreEvalInTT(int evalValue, byte nodeType, int depth)
    {
        _ttEntries[_board.ZobristKey % 16000000] = new TTEntry
        {
            _nodeType = nodeType,
            _depth = (byte)depth,
            _zobristKey = _board.ZobristKey,
            _evalValue = evalValue
        };
    }

    public struct TTEntry
    {
        public int _evalValue;
        public byte _depth;
        public byte _nodeType; // 0 = Exact, 1 = Alpha, 2 = Beta
        public ulong _zobristKey;
    }
}