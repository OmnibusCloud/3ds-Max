namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Quaternion / vector helpers for reasoning about how a DCC camera node will be oriented by the
/// Blender scene generator. The generator sets a Blender object rotation of
/// <c>nodeRotation @ axisCorrection</c> (axis correction = -90° about local X), and a Blender
/// camera looks down its local -Z. So the world-space forward of a synthesized camera is
/// <c>rotate(nodeRotation * axisCorrection, (0,0,-1))</c>. These helpers let the synthesizer both
/// check whether a candidate camera faces the geometry and build a look-at rotation that does.
/// </summary>
internal static class MaxCameraMath
{
    #region Constants

    // Quaternion((1,0,0), -90deg) as (W,X,Y,Z): the generator's camera/light local axis correction.
    private static readonly (double W, double X, double Y, double Z) AXIS_CORRECTION = (0.70710678118654752d, -0.70710678118654752d, 0d, 0d);

    // Inverse of the axis correction: Quaternion((1,0,0), +90deg).
    private static readonly (double W, double X, double Y, double Z) AXIS_CORRECTION_INVERSE = (0.70710678118654752d, 0.70710678118654752d, 0d, 0d);

    #endregion

    #region Functions

    /// <summary>
    /// Returns the world-space forward direction a camera node with the given rotation will have
    /// after the Blender generator applies its axis correction.
    /// </summary>
    public static (double X, double Y, double Z) ComputeGeneratorForward((double W, double X, double Y, double Z) nodeRotation)
    {
        var blenderRotation = Multiply(nodeRotation, AXIS_CORRECTION);
        return Rotate(blenderRotation, (0d, 0d, -1d));
    }

    /// <summary>
    /// Builds the camera node rotation that makes the generator-applied camera look along
    /// <paramref name="forward"/> with the given world up reference.
    /// </summary>
    public static (double W, double X, double Y, double Z) BuildLookAtNodeRotation((double X, double Y, double Z) forward, (double X, double Y, double Z) worldUp)
    {
        var blenderRotation = BuildLookAtRotation(forward, worldUp);
        // nodeRotation * axisCorrection == blenderRotation  =>  nodeRotation = blenderRotation * axisCorrection^-1
        return Multiply(blenderRotation, AXIS_CORRECTION_INVERSE);
    }

    public static (double X, double Y, double Z) Normalize((double X, double Y, double Z) v)
    {
        var length = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return length <= double.Epsilon ? (0d, 0d, 0d) : (v.X / length, v.Y / length, v.Z / length);
    }

    public static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }

    #endregion

    #region Tools

    private static (double W, double X, double Y, double Z) Multiply((double W, double X, double Y, double Z) a, (double W, double X, double Y, double Z) b)
    {
        return (
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);
    }

    private static (double X, double Y, double Z) Rotate((double W, double X, double Y, double Z) q, (double X, double Y, double Z) v)
    {
        var tx = 2d * (q.Y * v.Z - q.Z * v.Y);
        var ty = 2d * (q.Z * v.X - q.X * v.Z);
        var tz = 2d * (q.X * v.Y - q.Y * v.X);

        return (
            v.X + q.W * tx + (q.Y * tz - q.Z * ty),
            v.Y + q.W * ty + (q.Z * tx - q.X * tz),
            v.Z + q.W * tz + (q.X * ty - q.Y * tx));
    }

    private static (double W, double X, double Y, double Z) BuildLookAtRotation((double X, double Y, double Z) forward, (double X, double Y, double Z) worldUp)
    {
        // Blender camera convention: local -Z = forward, local +Y = up, local +X = right.
        var f = Normalize(forward);
        var zAxis = (-f.X, -f.Y, -f.Z); // camera local +Z points opposite the view direction

        var xAxis = Normalize(Cross(worldUp, zAxis));
        if (xAxis is { X: 0d, Y: 0d, Z: 0d })
            xAxis = Normalize(Cross((0d, 1d, 0d), zAxis)); // forward parallel to up: pick an alternate reference

        var yAxis = Cross(zAxis, xAxis);
        return MatrixToQuaternion(xAxis, yAxis, zAxis);
    }

    private static (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        return (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    }

    private static (double W, double X, double Y, double Z) MatrixToQuaternion((double X, double Y, double Z) col0, (double X, double Y, double Z) col1, (double X, double Y, double Z) col2)
    {
        // Columns are the rotated basis vectors (local->world). Standard matrix-to-quaternion.
        var m00 = col0.X; var m10 = col0.Y; var m20 = col0.Z;
        var m01 = col1.X; var m11 = col1.Y; var m21 = col1.Z;
        var m02 = col2.X; var m12 = col2.Y; var m22 = col2.Z;

        var trace = m00 + m11 + m22;

        if (trace > 0d)
        {
            var s = 0.5d / Math.Sqrt(trace + 1d);
            return (0.25d / s, (m21 - m12) * s, (m02 - m20) * s, (m10 - m01) * s);
        }

        if (m00 > m11 && m00 > m22)
        {
            var s = 2d * Math.Sqrt(1d + m00 - m11 - m22);
            return ((m21 - m12) / s, 0.25d * s, (m01 + m10) / s, (m02 + m20) / s);
        }

        if (m11 > m22)
        {
            var s = 2d * Math.Sqrt(1d + m11 - m00 - m22);
            return ((m02 - m20) / s, (m01 + m10) / s, 0.25d * s, (m12 + m21) / s);
        }

        var sz = 2d * Math.Sqrt(1d + m22 - m00 - m11);
        return ((m10 - m01) / sz, (m02 + m20) / sz, (m12 + m21) / sz, 0.25d * sz);
    }

    #endregion
}
