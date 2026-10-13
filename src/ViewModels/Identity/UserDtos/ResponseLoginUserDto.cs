namespace ViewModels.Identity.UserDtos
{
    public class ResponseLoginUserDto
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public string[] AccessList { get; set; }
    }
}
