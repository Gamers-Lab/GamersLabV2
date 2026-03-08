namespace BattleRecordsRouter.Helper;

public class InputValidationHelper
{
    /// <summary>
    /// Validates that a string is not null, empty, or whitespace.
    /// </summary>
    /// <param name="name">The string to validate.</param>
    /// <param name="errorMessage">Contains the error message if validation fails, otherwise empty.</param>
    /// <returns>True if the string is valid; otherwise, false.</returns>
    public static bool IsStringIsValid(string name, out string errorMessage)
    {
        errorMessage = string.Empty;

        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                errorMessage = "Input string cannot be empty";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"String validation error: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Validates that a Unix timestamp is within 48 hours of the current time (past or future).
    /// </summary>
    /// <param name="timestamp">The Unix timestamp to validate.</param>
    /// <param name="errorMessage">Contains the error message if validation fails, otherwise empty.</param>
    /// <returns>True if the timestamp is within the acceptable range; otherwise, false.</returns>
    public static bool IsTimestampValid(uint timestamp, out string errorMessage)
    {
        errorMessage = string.Empty;

        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var maxDrift = 60 * 60 * 48; // 48 hours in seconds

            if (timestamp < now - maxDrift)
            {
                errorMessage = "Timestamp is more than 48 hours in the past";
                return false;
            }

            if (timestamp > now + maxDrift)
            {
                errorMessage = "Timestamp is more than 48 hours in the future";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Timestamp validation error: {ex.Message}";
            return false;
        }
    }
}