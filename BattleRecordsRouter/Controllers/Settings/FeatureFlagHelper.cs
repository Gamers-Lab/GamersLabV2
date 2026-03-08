using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BattleRecordsRouter.Controllers.Settings;

/// <summary>
/// Shared helper class for feature flag evaluation logic.
/// Provides consistent feature flag checking across the application.
/// </summary>
public static class FeatureFlagHelper
{
    /// <summary>
    /// Determines if a feature is enabled based on its configuration value.
    /// Supports both boolean values and string "true"/"false" values.
    /// </summary>
    /// <param name="configuration">The configuration to check against</param>
    /// <param name="configKey">The full configuration key to check (e.g., "AppSettings:EnableAdminEndpoints")</param>
    /// <param name="logger">Optional logger for warnings when config keys are missing</param>
    /// <returns>True if the feature is enabled, false otherwise</returns>
    public static bool IsFeatureEnabled(IConfiguration configuration, string configKey, ILogger? logger = null)
    {
        var configValue = configuration[configKey];
        
        if (string.IsNullOrEmpty(configValue))
        {
            logger?.LogWarning("Configuration key {ConfigKey} is missing or empty, defaulting to disabled", configKey);
            return false;
        }
            
        // Try to parse as boolean
        if (bool.TryParse(configValue, out bool enabled))
            return enabled;
            
        // If not a boolean, check if it's "true" string
        return string.Equals(configValue, "true", StringComparison.OrdinalIgnoreCase);
    }
}

