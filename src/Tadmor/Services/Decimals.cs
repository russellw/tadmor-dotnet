using System.Globalization;

namespace Tadmor.Services;

public static class Decimals
{
    /// <summary>The value without trailing fractional zeros, as tadmor renders exchange rates ("1.125").</summary>
    public static decimal Trim(decimal d)
    {
        var s = d.ToString(CultureInfo.InvariantCulture);
        if (s.Contains('.'))
        {
            s = s.TrimEnd('0').TrimEnd('.');
        }
        return decimal.Parse(s, CultureInfo.InvariantCulture);
    }
}
