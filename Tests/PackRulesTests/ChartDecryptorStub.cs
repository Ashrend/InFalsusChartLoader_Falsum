namespace InFalsusChartLoader_falsum
{
    /// <summary>Test stub: readers only need Compile to return bytes; the real decryptor pulls IL2CPP.</summary>
    internal static class ChartDecryptor
    {
        internal static void DeriveKey(string seed, int count, ushort u16,
                                       out ulong s0, out ulong s1, out ulong s2, out ulong s3,
                                       out uint counter)
        {
            s0 = s1 = s2 = s3 = 0;
            counter = 0;
        }

        internal static ulong Gen(ulong s0, ulong s1, ulong s2, ulong s3, int idx, int k) => 0;

        internal static void Finalize(ref ulong s0, ref ulong s1, ref ulong s2, ref ulong s3,
                                      ref uint counter, ulong rdx, ulong r8, ulong r9) { }
    }
}
