namespace ViewModels.Identity.UserDtos
{
    public class RefreshTokenDto
    {
        public string RefreshToken { get; set; }
    }

    /// <summary>R1a2 (M6): the body of POST Accounts/logout; the refresh token is no longer sent in the address (addresses are written to server logs).</summary>
    public class LogoutDto
    {
        public string RefreshToken { get; set; }
    }
}
