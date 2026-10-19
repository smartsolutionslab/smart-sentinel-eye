using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 416 / issue #3023 item 6: a frozen copy of the audit's zone answer as it stood at <c>d24ee84e</c>
/// (<c>AuditPayloadRedaction.cs</c> lines 121-143 and 150-202), before the bracket scan folds onto
/// <see cref="UrlAuthority"/>. <c>DetailChanged</c>, <c>CarriesZone</c>, <c>BracketContentIsAddressOnly</c> and
/// <c>IsAddressText</c> are copied verbatim (only <c>private</c> becomes <c>internal</c>). The oracle calls the live
/// <see cref="UrlRedaction.ToOrigin"/>, <see cref="UrlRedaction.Unparseable"/> and the live locator: the fold touches
/// none of them, and forking them would test nothing. This file never changes after the characterisation commit.
/// </summary>
internal static class AuditPayloadBracketScanOracle
{
    internal static bool? DetailChanged(string? previousUrl, string? url)
    {
        if (previousUrl is null || url is null)
        {
            return false;
        }

        var previousOrigin = UrlRedaction.ToOrigin(previousUrl);
        var origin = UrlRedaction.ToOrigin(url);

        if (IsPlaceholder(previousOrigin) || IsPlaceholder(origin))
        {
            return null;
        }

        if (CarriesZone(previousUrl) || CarriesZone(url))
        {
            return null;
        }

        return string.Equals(previousOrigin, origin, StringComparison.Ordinal)
            && !string.Equals(previousUrl, url, StringComparison.Ordinal);
    }

    internal static bool CarriesZone(string url) =>
        !Uri.TryCreate(url, UriKind.Absolute, out var uri)
        || (uri.HostNameType == UriHostNameType.IPv6
            && (uri.DnsSafeHost.Contains('%') || !BracketContentIsAddressOnly(url)));

    internal static bool BracketContentIsAddressOnly(string url)
    {
        var authority = UrlAuthority.TextFromTheAuthority(url.AsSpan().Trim());

        if (authority.IsEmpty)
        {
            return false;
        }

        var terminator = UrlAuthority.IndexOfTheTerminator(authority);
        var userinfoEnd = (terminator < 0 ? authority : authority[..terminator]).LastIndexOf('@');

        if (userinfoEnd >= 0 && authority[..userinfoEnd].IndexOfAny("[]") >= 0)
        {
            return false;
        }

        var host = authority[(userinfoEnd + 1)..];

        if (host.IsEmpty || host[0] != '[')
        {
            return false;
        }

        var close = host.IndexOf(']');

        return close > 0 && IsAddressText(host[1..close]);
    }

    internal static bool IsAddressText(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        foreach (var character in text)
        {
            if (!char.IsAsciiHexDigit(character) && character is not (':' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsPlaceholder(string origin) =>
        string.Equals(origin, UrlRedaction.Unparseable, StringComparison.Ordinal);

    // Not a copy: the conditions under which the copied bracket scan runs (both origins rendering is the caller's
    // check), used only to count how many corpus inputs reach it.
    internal static bool IsBracketCheckReached(string url) =>
        !IsPlaceholder(UrlRedaction.ToOrigin(url))
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.HostNameType == UriHostNameType.IPv6
        && !uri.DnsSafeHost.Contains('%');

    // Not a copy: whether the copied scan's userinfo arm (a bracket before the last '@') is what answers.
    internal static bool HasBracketInTheUserinfo(string url)
    {
        var authority = UrlAuthority.TextFromTheAuthority(url.AsSpan().Trim());

        if (authority.IsEmpty)
        {
            return false;
        }

        var terminator = UrlAuthority.IndexOfTheTerminator(authority);
        var userinfoEnd = (terminator < 0 ? authority : authority[..terminator]).LastIndexOf('@');

        return userinfoEnd >= 0 && authority[..userinfoEnd].IndexOfAny("[]") >= 0;
    }
}
