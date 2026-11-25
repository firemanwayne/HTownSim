using System.Reflection;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace IncidentService.Api.Services;

/// <summary>
/// Lets MVC find controllers that are <c>internal</c> (the default finder only accepts public types), so the API can keep the
/// project convention of internal-by-default.
/// </summary>
internal sealed class InternalControllerFeatureProvider : ControllerFeatureProvider
{
    /// <inheritdoc />
    protected override bool IsController(TypeInfo typeInfo)
    {
        return typeInfo is { IsClass: true, IsAbstract: false }
            && typeInfo.Name.EndsWith("Controller", StringComparison.Ordinal)
            && typeInfo.IsDefined(typeof(ApiControllerAttribute), true);
    }
}
