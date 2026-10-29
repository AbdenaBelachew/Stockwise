using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StockWise.Application.Abstractions;
using Xunit;

namespace StockWise.IntegrationTests;

/// <summary>Helpers for driving the real HTTP API in tests.</summary>
public static class TestClient
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    public static async Task<HttpClient> LoginAsync(this StockWiseTestFactory factory, string email, string password)
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new("Bearer", body!.accessToken);
        return client;
    }

    public static async Task<HttpClient> RegisterAsync(this StockWiseTestFactory factory, string email, string password, string orgName, string userName = "Test User")
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = userName,
            email,
            password,
            organizationName = orgName,
            industry = "Retail",
            country = "Germany",
            timeZoneId = "UTC",
            currencyCode = "USD",
            dateFormat = "yyyy-MM-dd"
        });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new("Bearer", body!.accessToken);
        return client;
    }

    public record LoginResponse(string accessToken, Guid organizationId, string organizationName);

    public static async Task<JsonElement> PostJsonAsync(this HttpClient client, string url, object body)
    {
        var res = await client.PostAsJsonAsync(url, body);
        var content = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)res.StatusCode} on POST {url}: {content}");
        return JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
    }

    public static async Task<JsonElement> PutJsonAsync(this HttpClient client, string url, object body)
    {
        var res = await client.PutAsJsonAsync(url, body);
        var content = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)res.StatusCode} on PUT {url}: {content}");
        return JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
    }

    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string url)
    {
        var res = await client.GetAsync(url);
        var content = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)res.StatusCode} on GET {url}: {content}");
        return JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
    }
}
