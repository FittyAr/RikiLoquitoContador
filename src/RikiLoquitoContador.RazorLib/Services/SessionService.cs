using System;
using RikiLoquitoContador.Core.Services;

namespace RikiLoquitoContador.RazorLib.Services
{
    public interface ISessionService
    {
        bool IsAuthenticated { get; }
        event Action? OnStateChanged;
        bool Login(string password);
        void Logout();
    }

    public class SessionService : ISessionService
    {
        private readonly IConfigService _configService;
        private bool _isAuthenticated;

        public bool IsAuthenticated => _isAuthenticated;
        public event Action? OnStateChanged;

        public SessionService(IConfigService configService)
        {
            _configService = configService;
        }

        public bool Login(string password)
        {
            if (_configService.VerifyPassword(password))
            {
                _isAuthenticated = true;
                OnStateChanged?.Invoke();
                return true;
            }
            return false;
        }

        public void Logout()
        {
            _isAuthenticated = false;
            OnStateChanged?.Invoke();
        }
    }
}
