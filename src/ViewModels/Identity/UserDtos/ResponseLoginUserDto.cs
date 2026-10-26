namespace ViewModels.Identity.UserDtos
{
    public class ResponseLoginUserDto
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public string[] AccessList { get; set; }

        /// <summary>
        /// Empty after a normal sign-in. "MUST_CHANGE_PASSWORD" when the password is a temporary one set by an administrator:
        /// no tokens are given; the front-end asks for a new password and calls Accounts/change-temporary-password.
        /// </summary>
        public string Code { get; set; }

        public bool MustChangePassword { get; set; }
    }
}
