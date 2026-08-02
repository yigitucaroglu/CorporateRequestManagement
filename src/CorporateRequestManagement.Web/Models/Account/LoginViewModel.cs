using System.ComponentModel.DataAnnotations;
namespace CorporateRequestManagement.Web.Models.Account;
public sealed class LoginViewModel
{
    [Required(ErrorMessage = "E-posta zorunludur."), EmailAddress(ErrorMessage = "Geçerli bir e-posta giriniz.")]
    public string Email { get; set; } = string.Empty;
    [Required(ErrorMessage = "Parola zorunludur."), DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
    public override string ToString() => nameof(LoginViewModel);
}
