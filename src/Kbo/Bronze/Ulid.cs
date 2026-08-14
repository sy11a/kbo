namespace Kbo.Bronze;

public static class Ulid
{
    private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string New(DateTimeOffset time, Random random)
    {
        char[] encoded = new char[26];

        ulong timestampMilliseconds = (ulong)time.ToUnixTimeMilliseconds();
        for (int index = 9; index >= 0; index--)
        {
            encoded[index] = CrockfordAlphabet[(int)(timestampMilliseconds & 0x1F)];
            timestampMilliseconds >>= 5;
        }

        byte[] randomness = new byte[10];
        random.NextBytes(randomness);
        int bitBuffer = 0;
        int bitCount = 0;
        int position = 10;
        foreach (byte value in randomness)
        {
            bitBuffer = (bitBuffer << 8) | value;
            bitCount += 8;
            while (bitCount >= 5)
            {
                bitCount -= 5;
                encoded[position++] = CrockfordAlphabet[(bitBuffer >> bitCount) & 0x1F];
            }
        }

        return new string(encoded);
    }
}
