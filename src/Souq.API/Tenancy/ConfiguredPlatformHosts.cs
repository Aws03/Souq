using Microsoft.Extensions.Options;
using Souq.Application.Common.Tenancy;

namespace Souq.API.Tenancy;

// مضيفو المنصّة كما يراهم تحديد المستأجر نفسه (TenancyOptions بعد PostConfigure) — مصدر واحد للجوابين.
public sealed class ConfiguredPlatformHosts : IPlatformHosts
{
    private readonly IOptionsMonitor<TenancyOptions> _options;
    public ConfiguredPlatformHosts(IOptionsMonitor<TenancyOptions> options) => _options = options;

    public bool IsPlatformHost(string host) =>
        _options.CurrentValue.PlatformHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
}
