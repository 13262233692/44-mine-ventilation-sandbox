using System;
using System.Collections.Generic;

namespace MineVentilation.Core
{
    public class SparseMatrix
    {
        private int _rows;
        private int _cols;
        private Dictionary<(int, int), double> _entries;

        public int Rows => _rows;
        public int Cols => _cols;

        public SparseMatrix(int rows, int cols)
        {
            _rows = rows;
            _cols = cols;
            _entries = new Dictionary<(int, int), double>();
        }

        public double this[int row, int col]
        {
            get => _entries.TryGetValue((row, col), out var val) ? val : 0.0;
            set
            {
                if (Math.Abs(value) < 1e-15)
                {
                    _entries.Remove((row, col));
                }
                else
                {
                    _entries[(row, col)] = value;
                }
            }
        }

        public void Add(int row, int col, double value)
        {
            if (Math.Abs(value) < 1e-15) return;
            var key = (row, col);
            if (_entries.TryGetValue(key, out var existing))
            {
                double sum = existing + value;
                if (Math.Abs(sum) < 1e-15)
                    _entries.Remove(key);
                else
                    _entries[key] = sum;
            }
            else
            {
                _entries[key] = value;
            }
        }

        public int NonZeroCount => _entries.Count;

        public double[] Solve(double[] rhs)
        {
            if (_rows != _cols)
                throw new InvalidOperationException("Matrix must be square for solving");

            int n = _rows;
            if (rhs.Length != n)
                throw new ArgumentException("RHS dimension mismatch");

            double[,] dense = ToDense();
            double[] b = (double[])rhs.Clone();

            LUDecompose(dense, n);
            double[] x = LUSolve(dense, b, n);

            return x;
        }

        double[,] ToDense()
        {
            var dense = new double[_rows, _cols];
            foreach (var kv in _entries)
            {
                dense[kv.Key.Item1, kv.Key.Item2] = kv.Value;
            }
            return dense;
        }

        void LUDecompose(double[,] a, int n)
        {
            int[] pivot = new int[n];
            for (int i = 0; i < n; i++) pivot[i] = i;

            for (int k = 0; k < n; k++)
            {
                double maxVal = 0.0;
                int maxRow = k;
                for (int i = k; i < n; i++)
                {
                    if (Math.Abs(a[i, k]) > maxVal)
                    {
                        maxVal = Math.Abs(a[i, k]);
                        maxRow = i;
                    }
                }

                if (maxVal < 1e-14)
                {
                    a[k, k] = 1e-10;
                    continue;
                }

                if (maxRow != k)
                {
                    for (int j = 0; j < n; j++)
                    {
                        double tmp = a[k, j];
                        a[k, j] = a[maxRow, j];
                        a[maxRow, j] = tmp;
                    }
                    int tmpP = pivot[k];
                    pivot[k] = pivot[maxRow];
                    pivot[maxRow] = tmpP;
                }

                for (int i = k + 1; i < n; i++)
                {
                    if (Math.Abs(a[k, k]) < 1e-14) continue;
                    a[i, k] /= a[k, k];
                    for (int j = k + 1; j < n; j++)
                    {
                        a[i, j] -= a[i, k] * a[k, j];
                    }
                }
            }
        }

        double[] LUSolve(double[,] lu, double[] b, int n)
        {
            double[] x = new double[n];
            double[] y = new double[n];

            for (int i = 0; i < n; i++)
            {
                y[i] = b[i];
                for (int j = 0; j < i; j++)
                {
                    y[i] -= lu[i, j] * y[j];
                }
            }

            for (int i = n - 1; i >= 0; i--)
            {
                x[i] = y[i];
                for (int j = i + 1; j < n; j++)
                {
                    x[i] -= lu[i, j] * x[j];
                }
                if (Math.Abs(lu[i, i]) < 1e-14)
                    x[i] = 0.0;
                else
                    x[i] /= lu[i, i];
            }

            return x;
        }

        public void Clear()
        {
            _entries.Clear();
        }

        public SparseMatrix Clone()
        {
            var clone = new SparseMatrix(_rows, _cols);
            foreach (var kv in _entries)
            {
                clone._entries[kv.Key] = kv.Value;
            }
            return clone;
        }
    }

    public static class LinearSolver
    {
        public static double[] Solve(SparseMatrix A, double[] b)
        {
            return A.Solve(b);
        }

        public static double[] SolveWithRegularization(SparseMatrix A, double[] b, double lambda)
        {
            int n = A.Rows;
            var reg = A.Clone();
            for (int i = 0; i < n; i++)
            {
                reg.Add(i, i, lambda);
            }
            return reg.Solve(b);
        }
    }
}
