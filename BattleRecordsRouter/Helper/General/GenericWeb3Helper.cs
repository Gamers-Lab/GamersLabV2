using Nethereum.Web3.Accounts;
using Nethereum.Util;
using Nethereum.Hex.HexConvertors.Extensions;

namespace BattleRecordsRouter.Helper;

public static class GenericWeb3Helper
{
    /// <summary>
    /// Generates a new random Ethereum address and private key.
    /// </summary>
    /// <returns>A tuple containing (address, privateKey)</returns>
    public static (string address, string privateKey) GenerateNewAccount()
    {
        // Create a new Nethereum account with random private key
        var ecKey = Nethereum.Signer.EthECKey.GenerateKey();
        var privateKey = ecKey.GetPrivateKeyAsBytes().ToHex(prefix: true);

        // Create account from private key to get the address
        var account = new Account(privateKey);
        var address = account.Address;

        return (address, privateKey);
    }

    /// <summary>
    /// Validates that a string is a properly formatted Ethereum address.
    /// </summary>
    /// <param name="address">The Ethereum address to validate.</param>
    /// <param name="errorMessage">Contains the error message if validation fails, otherwise empty.</param>
    /// <returns>True if the address is valid; otherwise, false.</returns>
    public static bool IsValidAddress(string address, out string errorMessage)
    {
        errorMessage = string.Empty;

        try
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                errorMessage = "Address cannot be empty";
                return false;
            }

            // Use Nethereum's address validation
            var isValid = Nethereum.Util.AddressUtil.Current.IsValidEthereumAddressHexFormat(address);
            if (!isValid)
            {
                errorMessage = "Invalid Ethereum address format";
                return false;
            }

            // Additional check for correct address length
            var addressNoPrefix = address.StartsWith("0x") ? address[2..] : address;
            if (addressNoPrefix.Length != 40)
            {
                errorMessage = "Ethereum address must be 40 characters long (excluding '0x' prefix)";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Address validation error: {ex.Message}";
            return false;
        }
    }
}