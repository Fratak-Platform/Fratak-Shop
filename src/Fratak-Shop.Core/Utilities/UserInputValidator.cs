using System.Globalization;
using System.Text.RegularExpressions;

namespace Fratak_Shop.Core.Utilities;

public static class UserInputValidator
{
    public static bool IsValidMobile(this string mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile))
            return false;

        mobile = mobile.Trim();

        return Regex.IsMatch(mobile, @"^\d+$") && Regex.IsMatch(mobile, @"^09\d{9}$");
    }

    public static bool IsValidEmail(this string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            email = Regex.Replace(email, @"(@)(.+)$", DomainMapper,
                RegexOptions.None, TimeSpan.FromMilliseconds(200));

            static string DomainMapper(Match match)
            {
                var idn = new IdnMapping();
                var domainName = idn.GetAscii(match.Groups[2].Value);
                return match.Groups[1].Value + domainName;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(email,
                @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
                RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    public static bool IsValidPhone(this string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return false;

        try
        {
            return Regex.IsMatch(
                phone.Trim(),
                @"^0\d{2}\d{8}$",
                RegexOptions.None,
                TimeSpan.FromMilliseconds(250));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }


    public static bool IsValidCardNumber(this string cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
            return false;

        try
        {
            return Regex.IsMatch(
                cardNumber.Trim(),
                @"^\d{16}$",
                RegexOptions.None,
                TimeSpan.FromMilliseconds(250));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    public static bool IsValidAccountNumber(this string accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
            return false;

        try
        {
            return Regex.IsMatch(
                accountNumber.Trim(),
                @"^\d+$",
                RegexOptions.None,
                TimeSpan.FromMilliseconds(250));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    public static bool IsValidShabaNumber(this string shabaNumber)
    {
        if (string.IsNullOrWhiteSpace(shabaNumber))
            return false;

        try
        {
            return Regex.IsMatch(
                shabaNumber.Trim(),
                @"^IR\d{24}$",
                RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(250));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
