using System;
using System.Diagnostics;
using System.Text;

namespace StackExchange.Redis;

internal sealed partial class ServerSelectionStrategy
{
    // pre-computed hash-tags for each slot
    private static class HashTags
    {
        private static readonly string[] Cache = Populate();
        public static ReadOnlySpan<string> Tags => Cache;
        public static string Get(int slot) => Cache[slot];

        private static string[] Populate()
        {
            // Via testing, we know that 3 characters is sufficient to populate all slots
            // using a total of 48643 operations - this is acceptable (same order-of-magnitude as the slot count).
            var slots = new string?[TotalSlots];

            // using an alphabet of the visible ASCII characters, excluding { and } (used to denote hash-tags)
            ReadOnlySpan<byte> alphabet = "!\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz|~"u8;
            Debug.WriteLine($"Alphabet: '{Encoding.ASCII.GetString(alphabet)}', {alphabet.Length} chars");

            Span<byte> threeChars = stackalloc byte[3];
            Span<byte> twoChars = threeChars.Slice(0, 2);
            Span<byte> oneChar = threeChars.Slice(0, 1);

            int operations = 0;
            int remaining = slots.Length;

            bool Test(ReadOnlySpan<byte> span)
            {
                var slot = GetClusterSlot(span);
                operations++;
                if (slots[slot] is { } existing)
                {
                    // prefer smaller tags (but doesn't change the outcome)
                    if (span.Length < existing.Length)
                    {
                        slots[slot] = Encoding.ASCII.GetString(span);
                    }
                }
                else
                {
                    // new value for this slot
                    slots[slot] = Encoding.ASCII.GetString(span);
                    return --remaining == 0;
                }

                return false;
            }
            for (int i = 0; i < alphabet.Length; i++)
            {
                oneChar[0] = alphabet[i];

                // Test single character keys
                if (i == 0) Test(oneChar);

                for (int j = 0; j < alphabet.Length; j++)
                {
                    twoChars[1] = alphabet[j];

                    // Test two characters keys
                    if (i == 0) Test(twoChars);

                    for (int k = 0; k < alphabet.Length; k++)
                    {
                        threeChars[2] = alphabet[k];

                        // Test three characters - we know this is the only possible exit location
                        if (Test(threeChars))
                        {
                            Debug.WriteLine($"Populated all hash-tag slots in {operations} operations");
                            return slots!;
                        }
                    }
                }
            }

            throw new InvalidOperationException(
                $"Failed to populate hash-tag cache after {operations} operations, {remaining} slots remaining");
        }
    }
}
