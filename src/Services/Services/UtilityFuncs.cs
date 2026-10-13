using System;
using System.Text.RegularExpressions;
using Entities.Model;

namespace Services.Services
{
    public static class UtilityFuncs
    {
        public static bool IsMobileNumber(string number)
        {
            try
            {
                if (string.IsNullOrEmpty(number))
                    return false;

                var r = new Regex(@"^(?:0|98|\+98|\+980|0098|098|00980)?(9\d{9})$");
                return r.IsMatch(number);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static PasswordScore IsPasswordStrength(string password)
        {
            int score = 1;
            if (password.Length < 1)
                return PasswordScore.Blank;

            if (password.Length < 4)
                return PasswordScore.VeryWeak;

            if (password.Length >= 8) score++;

            if (password.Length >= 12) score++;

            if (Regex.IsMatch(password, @"[0-9]+(\.[0-9][0-9]?)?"))   //number only //"^\d+$" if you need to match more than one digit.
                score++;

            if (Regex.IsMatch(password, @"^(?=.*[a-z])(?=.*[A-Z]).+$")) //both, lower and upper case
                score++;

            if (Regex.IsMatch(password, @"[!,@,#,$,%,^,&,*,?,_,~,-,£,(,)]")) //^[A-Z]+$
                score++;

            return (PasswordScore)score;
        }

    }




}
