namespace BattleRecordsRouter.Controllers.Settings;

using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Logging;

/// <summary>
/// Attribute that marks controllers or actions as only available when a specific configuration flag is enabled.
/// Applied to controllers or actions to conditionally include/exclude them based on configuration settings.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class OnlyInEnvironmentAttribute : Attribute
{
    /// <summary>
    /// The configuration key to check in AppSettings section
    /// </summary>
    public string ConfigKey { get; }
    
    /// <summary>
    /// Creates a new instance of the attribute with the specified configuration key
    /// </summary>
    /// <param name="configKey">The key to check in AppSettings section</param>
    public OnlyInEnvironmentAttribute(string configKey) => ConfigKey = configKey;
}

/// <summary>
/// Convention that dynamically removes controllers or actions from routing and Swagger
/// when the matching configuration flag is disabled. Works with the OnlyInEnvironmentAttribute
/// to provide feature flagging at the API endpoint level.
/// </summary>
public sealed class OnlyInEnvironmentConvention : IControllerModelConvention, IActionModelConvention
{
    private readonly IConfiguration _cfg;
    private readonly ILogger<OnlyInEnvironmentConvention>? _logger;

    /// <summary>
    /// Creates a new instance of the convention with the required configuration
    /// </summary>
    /// <param name="cfg">Configuration to check feature flags against</param>
    /// <param name="logger">Optional logger for debugging and information messages</param>
    public OnlyInEnvironmentConvention(IConfiguration cfg, ILogger<OnlyInEnvironmentConvention>? logger = null)
    {
        _cfg = cfg;
        _logger = logger;
    }

    /// <summary>
    /// Applies the convention to controllers, removing them from routing and Swagger
    /// if their associated feature flag is disabled
    /// </summary>
    /// <param name="controller">The controller model to check and potentially modify</param>
    public void Apply(ControllerModel controller)
    {
        var attr = controller.Attributes.OfType<OnlyInEnvironmentAttribute>().FirstOrDefault();
        if (attr is null) return;

        string controllerName = controller.ControllerType.Name;
        string configKey = $"AppSettings:{attr.ConfigKey}";

        bool enabled = FeatureFlagHelper.IsFeatureEnabled(_cfg, configKey, _logger);

        _logger?.LogDebug("Controller {Controller} with config key {ConfigKey}, enabled: {Enabled}",
            controllerName, configKey, enabled);

        if (!enabled)
        {
            _logger?.LogInformation("Disabling controller {Controller} based on config {ConfigKey}",
                controllerName, configKey);

            // Hide from Swagger
            controller.ApiExplorer.IsVisible = false;

            // Remove all route selectors to prevent routing
            controller.Selectors.Clear();
        }
    }

    /// <summary>
    /// Applies the convention to actions, removing them from routing and Swagger
    /// if their associated feature flag is disabled
    /// </summary>
    /// <param name="action">The action model to check and potentially modify</param>
    public void Apply(ActionModel action)
    {
        var attr = action.Attributes.OfType<OnlyInEnvironmentAttribute>().FirstOrDefault();
        if (attr is null) return;

        string actionName = action.ActionName;
        string controllerName = action.Controller.ControllerName;
        string configKey = $"AppSettings:{attr.ConfigKey}";

        bool enabled = FeatureFlagHelper.IsFeatureEnabled(_cfg, configKey, _logger);

        _logger?.LogDebug("Action {Controller}.{Action} with config key {ConfigKey}, enabled: {Enabled}",
            controllerName, actionName, configKey, enabled);

        if (!enabled)
        {
            _logger?.LogInformation("Disabling action {Controller}.{Action} based on config {ConfigKey}",
                controllerName, actionName, configKey);

            // Hide from Swagger
            action.ApiExplorer.IsVisible = false;

            // Remove all route selectors to prevent routing
            action.Selectors.Clear();
        }
    }

}