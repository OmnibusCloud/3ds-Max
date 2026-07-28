using OutWit.Cloud.Auth.Interfaces;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

internal sealed class FakeAuthorizationCallbackListenerFactory : IAuthorizationCallbackListenerFactory
{
    #region Fields

    private readonly IAuthorizationCallbackListener m_listener;

    #endregion

    #region Constructors

    public FakeAuthorizationCallbackListenerFactory(IAuthorizationCallbackListener listener)
    {
        m_listener = listener;
    }

    #endregion

    #region IAuthorizationCallbackListenerFactory

    public IAuthorizationCallbackListener Create()
    {
        return m_listener;
    }

    #endregion
}
