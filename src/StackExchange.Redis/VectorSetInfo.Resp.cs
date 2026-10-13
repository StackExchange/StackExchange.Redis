using RESPite.Messages;

namespace StackExchange.Redis;

public readonly partial struct VectorSetInfo
{
    /// <summary>
    /// Read a <c>VINFO</c> reply: an attribute map, whose keys are named rather than positional.
    /// </summary>
    /// <param name="reader">The reader, positioned on the top-level aggregate.</param>
    /// <param name="info">The parsed info, when this returns <see langword="true"/>.</param>
    /// <remarks>
    /// <para>
    /// Shared by both readers - the <c>ResultProcessor</c> path and the interpolated surface's handler -
    /// rather than restated in each. The reason is in the shape: every field is optional and unknown ones
    /// are skipped, so a reader that falls behind does not fail, it silently returns defaults.
    /// </para>
    /// <para>
    /// A nil reply is "no such key" and reads as <see langword="false"/> with no value, which is what
    /// makes the result nullable on both surfaces.
    /// </para>
    /// </remarks>
    internal static bool TryRead(ref RespReader reader, out VectorSetInfo info)
    {
        info = default;
        if (!reader.IsAggregate || reader.IsNull) return false;

        var quantType = VectorSetQuantization.Unknown;
        string? quantTypeRaw = null;
        int vectorDim = 0, maxLevel = 0;
        long resultSize = 0, vsetUid = 0, hnswMaxNodeUid = 0;

        while (reader.TryMoveNext())
        {
            if (!reader.IsScalar) break;

            VectorSetInfoField field;
            unsafe
            {
                if (!reader.TryParseScalar(&VectorSetInfoFieldMetadata.TryParse, out field))
                {
                    field = VectorSetInfoField.Unknown;
                }
            }

            if (!reader.TryMoveNext()) break;

            // a non-scalar value is a field this version does not know about; skip it rather than stop
            if (!reader.IsScalar)
            {
                reader.SkipChildren();
                continue;
            }

            unsafe
            {
                switch (field)
                {
                    case VectorSetInfoField.Size when reader.TryReadInt64(out var i64):
                        resultSize = i64;
                        break;
                    case VectorSetInfoField.VsetUid when reader.TryReadInt64(out var i64):
                        vsetUid = i64;
                        break;
                    case VectorSetInfoField.MaxLevel when reader.TryReadInt64(out var i64):
                        maxLevel = checked((int)i64);
                        break;
                    case VectorSetInfoField.VectorDim when reader.TryReadInt64(out var i64):
                        vectorDim = checked((int)i64);
                        break;
                    case VectorSetInfoField.QuantType
                        when reader.TryParseScalar(
                                 &VectorSetQuantizationMetadata.TryParse,
                                 out VectorSetQuantization quantTypeValue)
                             && quantTypeValue is not VectorSetQuantization.Unknown:
                        quantType = quantTypeValue;
                        break;
                    case VectorSetInfoField.QuantType:
                        quantTypeRaw = reader.ReadString();
                        quantType = VectorSetQuantization.Unknown;
                        break;
                    case VectorSetInfoField.HnswMaxNodeUid when reader.TryReadInt64(out var i64):
                        hnswMaxNodeUid = i64;
                        break;
                }
            }
        }

        info = new VectorSetInfo(quantType, quantTypeRaw, vectorDim, resultSize, maxLevel, vsetUid, hnswMaxNodeUid);
        return true;
    }
}
