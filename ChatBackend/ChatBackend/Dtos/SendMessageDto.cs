namespace ChatBackend.Dtos
{
    public class SendMessageDto
    {
        public int UserId { get; set; }
        public string Text { get; set; } = string.Empty;
    }
}
