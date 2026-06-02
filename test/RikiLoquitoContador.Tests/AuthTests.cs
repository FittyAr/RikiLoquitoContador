using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using RikiLoquitoContador.Core.Services;
using Xunit;

namespace RikiLoquitoContador.Tests
{
    public class AuthTests
    {
        [Fact]
        public void HashPassword_ShouldGenerateValidBcryptHash()
        {
            // Arrange
            var inMemorySettings = new Dictionary<string, string?> {
                {"SecuritySettings:PasswordHash", ""}
            };
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            var configService = new ConfigService(configuration);

            // Act
            var password = "TestPassword123";
            var hash = configService.HashPassword(password);

            // Assert
            Assert.NotNull(hash);
            Assert.StartsWith("$2a$", hash);
            Assert.Equal(60, hash.Length);
        }

        [Fact]
        public void VerifyPassword_ShouldReturnTrueForCorrectPassword()
        {
            // Arrange
            var password = "contadorPassword";
            
            // Hash it first
            var tempConfig = new ConfigurationBuilder().Build();
            var service = new ConfigService(tempConfig);
            var hash = service.HashPassword(password);

            var inMemorySettings = new Dictionary<string, string?> {
                {"SecuritySettings:PasswordHash", hash}
            };
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            var configService = new ConfigService(configuration);

            // Act
            var resultCorrect = configService.VerifyPassword(password);
            var resultIncorrect = configService.VerifyPassword("wrongPassword");

            // Assert
            Assert.True(resultCorrect);
            Assert.False(resultIncorrect);
        }
    }
}
