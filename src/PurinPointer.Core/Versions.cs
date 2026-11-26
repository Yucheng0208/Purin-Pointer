namespace PurinPointer.Core;

public static class Versions
{
    /// <summary>"v0.2.0" is newer than "0.1.1", comparing numeric parts.</summary>
    public static bool IsNewer(string remote, string local)
    {
        // TrimStart also drops the dot of tags written "v.0.1.0", so they read as 0.1.0, not 0.0.1.0.
        static int[] Parts(string v) => v.Trim().TrimStart('v', 'V', '.').Split('.')
            .Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0).ToArray();
        var a = Parts(remote); var b = Parts(local);
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }
}
