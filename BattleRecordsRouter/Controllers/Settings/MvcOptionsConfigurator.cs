using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Options;

namespace BattleRecordsRouter.Controllers.Settings;

/// <summary>
/// Configures MVC options to include feature flag conventions and global authorization.
/// Registers the OnlyInEnvironmentConvention for both controller and action-level feature flagging,
/// and adds a global AuthorizeFilter to require authentication by default.
/// </summary>
public class MvcOptionsConfigurator : IConfigureOptions<MvcOptions>
{
    private readonly OnlyInEnvironmentConvention _convention;

    /// <summary>
    /// Initializes a new instance of the MvcOptionsConfigurator
    /// </summary>
    /// <param name="convention">The feature flag convention to apply to controllers and actions</param>
    public MvcOptionsConfigurator(OnlyInEnvironmentConvention convention)
    {
        _convention = convention;
    }

    /// <summary>
    /// Configures MVC options by adding feature flag conventions and global authorization filter
    /// </summary>
    /// <param name="options">The MVC options to configure</param>
    public void Configure(MvcOptions options)
    {
        // Register convention for both controller and action processing
        // The explicit casts ensure the convention is invoked for both types
        options.Conventions.Add((IControllerModelConvention)_convention);
        options.Conventions.Add((IActionModelConvention)_convention);

        // Add global authorization filter - all endpoints require authentication by default
        options.Filters.Add(new AuthorizeFilter());
    }
}