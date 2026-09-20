using System.Text.Json;

namespace Sense.Modules.Employee.Api.Controllers;

/// <summary>
/// <c>PUT /employees/me/profile</c> tam-değiştirme semantiğindedir (alan gönderilmemiş = null = temizle). Bu yüzden boş gövde
/// (<c>{}</c>) mevcut iletişim verisini sessizce silerdi. Alanları null'a eşlenen kayıt tipi "eksik" ile "açıkça null"u ayırt
/// edemediğinden, gövdede hiçbir özellik yoksa istek reddedilir; web formu daima tüm anahtarları (null dahil) gönderir.
/// </summary>
public static class MyProfileRequestParser
{
    public static UpdateMyProfileRequest? Parse(JsonElement body, JsonSerializerOptions options)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.EnumerateObject().Any())
        {
            return null;
        }

        try
        {
            return body.Deserialize<UpdateMyProfileRequest>(options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
