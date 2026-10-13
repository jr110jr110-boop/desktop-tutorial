namespace ViewModels.NasbNiro
{
    public abstract class DeleteEntityResultDtoBase<KEY>
    {
        public KEY Id { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
