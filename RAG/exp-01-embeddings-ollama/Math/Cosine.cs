namespace Exp01EmbeddingsOllama.Math;

public static class Cosine
{
    public static float[] Normalize(float[] vector)
    {
        if (vector.Length == 0)
        {
            throw new ArgumentException("El vector no puede estar vacio.", nameof(vector));
        }

        double sumSquares = 0;
        for (var i = 0; i < vector.Length; i++)
        {
            sumSquares += vector[i] * vector[i];
        }

        if (sumSquares == 0)
        {
            throw new InvalidOperationException("No se puede normalizar un vector de norma cero.");
        }

        var norm = System.Math.Sqrt(sumSquares);
        var normalized = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            normalized[i] = (float)(vector[i] / norm);
        }

        return normalized;
    }

    public static double Dot(float[] left, float[] right)
    {
        if (left.Length != right.Length)
        {
            throw new InvalidOperationException(
                $"Dimension incompatible para dot product: {left.Length} vs {right.Length}.");
        }

        double dot = 0;
        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
        }

        return dot;
    }
}
