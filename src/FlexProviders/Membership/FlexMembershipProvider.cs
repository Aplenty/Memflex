using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web;
using DotNetOpenAuth.AspNet;
using Microsoft.Web.WebPages.OAuth;

namespace FlexProviders.Membership
{
    public class FlexMembershipProvider<TUser> : 
        IFlexMembershipProvider<TUser>,
        IFlexOAuthProvider<TUser>, 
        IOpenAuthDataProvider
        where TUser: class, IFlexMembershipUser
    {
        private const int TokenSizeInBytes = 16;

        private static readonly Dictionary<string, AuthenticationClientData> _authenticationClients =
            new Dictionary<string, AuthenticationClientData>(StringComparer.OrdinalIgnoreCase);

        private readonly IApplicationEnvironment _applicationEnvironment;
        private readonly ISecurityEncoder _encoder = new DefaultSecurityEncoder();
        private readonly IFlexUserStore<TUser> _userStore;

        /// <summary>
        /// Initializes a new instance of the <see cref="FlexMembershipProvider" /> class.
        /// </summary>
        /// <param name="userStore">The user store.</param>
        /// <param name="applicationEnvironment">The application environment.</param>
        public FlexMembershipProvider(
            IFlexUserStore<TUser> userStore,
            IApplicationEnvironment applicationEnvironment)
        {
            _userStore = userStore;
            _applicationEnvironment = applicationEnvironment;
        }

		#region IFlexMembershipProvider Members

		/// <summary>
		/// Determines whether the provided <paramref name="username"/> and
		/// <paramref name="password"/> combination is valid
		/// </summary>
		/// <param name="username">The username.</param>
		/// <param name="password">The password.</param>
		/// <param name="rememberMe">
		/// if set to <c>true</c> [remember me].
		/// </param>
		/// <param name="license">The license the user belongs to.</param>
		/// <returns>
		/// 
		/// </returns>
		public bool Login(string username, string password, bool rememberMe = false, string license = null)
        {
            IFlexMembershipUser user = _userStore.GetUserByUsername(username, license);
            if (user == null)
            {
                return false;
            }

            string encodedPassword = _encoder.Encode(password, user.Salt);
            bool passed = encodedPassword.Equals(user.Password);
            if (passed)
            {
                _applicationEnvironment.IssueAuthTicket(username, rememberMe);
                return true;
            }
            return false;
        }

		public bool LoginSso(string token, bool rememberMe = false)
		{
			IFlexMembershipUser user = _userStore.GetUserBySsoToken(token);
			if (user == null)
			{
				return false;
			}

			//If we found a user for the token we will allow the login
			_applicationEnvironment.IssueAuthTicket(user.Username, rememberMe);
			return true;
			
		}

		/// <summary>
		///   Logout the current user
		/// </summary>
		public void Logout()
        {
            _applicationEnvironment.RevokeAuthTicket();
        }

        /// <summary>
        ///   Creates an account.
        /// </summary>
        /// <param name="user"> The user. </param>
        public void CreateAccount(TUser user)
        {
			IFlexMembershipUser existingUser = null;

            existingUser = _userStore.GetUserByUsername(user.Username, user.License);
            if (existingUser != null)
            {
                throw new FlexMembershipException(FlexMembershipStatus.DuplicateUserName);
            }

			//Email is not required, but if used cannot collide with any other users email (in the license)
			if (!string.IsNullOrEmpty(user.Username))
	        {
				existingUser = _userStore.GetUserByUsername(user.Username, user.License);
				if (existingUser != null)
				{
					throw new FlexMembershipException(FlexMembershipStatus.DuplicateEmail);
				}    
	        }
			

            user.Salt = user.Salt ?? _encoder.GenerateSalt();
            user.Password = _encoder.Encode(user.Password, user.Salt);
            _userStore.Add(user);
        }

        /// <summary>
        ///   Updates the account.
        /// </summary>
        /// <param name="user"> The user. </param>
		public void UpdateAccount(TUser user)
        {
	        IFlexMembershipUser existingUser = null;

			//Check if the username is taken by someone else
			existingUser = _userStore.GetUserByUsername(user.Username, user.License);
			if (existingUser != null && existingUser != user)
			{
				throw new FlexMembershipException("UpdateAccount failed because there is another account with that username.");
			}

			//Email is not required, but if used cannot collide with any other users email (in the license)
			if (!string.IsNullOrEmpty(user.Username))
	        {
				existingUser = _userStore.GetUserByUsername(user.Username, user.License);
				if (existingUser != null && existingUser != user)
				{
					throw new FlexMembershipException(FlexMembershipStatus.DuplicateEmail);
				}
	        }

			_userStore.Save(user);
		}

		/// <summary>
		///   Determines whether the specific <paramref name="username" /> has a
		///   local account
		/// </summary>
		/// <param name="username"> The username. </param>
		/// <param name="license">The license the user belongs to.</param>
		/// <returns> <c>true</c> if the specified username has a local account; otherwise, <c>false</c> . </returns>
		public bool HasLocalAccount(string userName, string license = null)
        {
            IFlexMembershipUser user = _userStore.GetUserByUsername(userName, license);
            return user != null && !String.IsNullOrEmpty(user.Password);
        }

        public bool Exists(string userName, string license = null)
        {
            IFlexMembershipUser user = _userStore.GetUserByUsername(userName, license);
            return user != null;
        }

        /// <summary>
        ///   Changes the password for a user
        /// </summary>
        /// <param name="username"> The username. </param>
        /// <param name="oldPassword"> The old password. </param>
        /// <param name="newPassword"> The new password. </param>
        /// <returns> </returns>
        public bool ChangePassword(string username, string oldPassword, string newPassword, string license = null)
        {
            TUser user = _userStore.GetUserByUsername(username, license);
            string encodedPassword = _encoder.Encode(oldPassword, user.Salt);
            if (!encodedPassword.Equals(user.Password))
            {
                return false;
            }

            user.Password = _encoder.Encode(newPassword, user.Salt);
            _userStore.Save(user);
            return true;
        }

        /// <summary>
        ///   Sets the local password for a user
        /// </summary>
        /// <param name="username"> The username. </param>
        /// <param name="newPassword"> The new password. </param>
        public void SetLocalPassword(string username, string newPassword, string license = null)
        {
            TUser user = _userStore.GetUserByUsername(username, license);
            if (!String.IsNullOrEmpty(user.Password))
            {
                throw new FlexMembershipException("SetLocalPassword can only be used on accounts that currently don't have a local password.");
            }

            user.Salt = _encoder.GenerateSalt();
            user.Password = _encoder.Encode(newPassword, user.Salt);
            _userStore.Save(user);
        }

		/// <summary>
		///   Generates the password reset token for a user
		/// </summary>
		/// <param name="username"> The username. </param>
		/// <param name="tokenExpirationInMinutesFromNow"> The token expiration in minutes from now. </param>
		/// <param name="license">The license the user belongs to.</param>
		/// <param name="forceRegeneration">If true a new token will be created with a new end time overwriting (and thus invalidating any previous token). If false a new token is generated when no token is set or current token is expired otherwise the current token is returned. When fetching current token it will adjust the token expiration time but only if the new time would be further into the future.</param>
		/// <returns> </returns>
		public string GeneratePasswordResetToken(string username, int tokenExpirationInMinutesFromNow = 1440, string license = null, bool forceRegeneration = false)
        {
            TUser user = _userStore.GetUserByUsername(username, license);
            if (user == null)
            {
                throw new FlexMembershipException(FlexMembershipStatus.InvalidUserName);
            }

			var tokenExpiration = DateTime.UtcNow.AddMinutes(tokenExpirationInMinutesFromNow);
			string returnToken = null;

			//If we don't have a current token or it's expired we force regeneration (alternatively we can force it anyway)
			if (forceRegeneration || string.IsNullOrEmpty(user.PasswordResetToken) || user.PasswordResetTokenExpiration < DateTime.UtcNow)
			{
				user.PasswordResetToken = GenerateToken();
				user.PasswordResetTokenExpiration = tokenExpiration;
			}
			else if (user.PasswordResetTokenExpiration < tokenExpiration)
			{
				//We are going to reuse a token but it would expire earlier than the token we asked for so we extend the expiration time
				user.PasswordResetTokenExpiration = tokenExpiration;
			}

            _userStore.Save(user);

            return user.PasswordResetToken;
        }

        /// <summary>
        ///   Resets the password for the supplied
        ///   <paramref name="passwordResetToken" />
        /// </summary>
        /// <param name="passwordResetToken"> The password reset token to perform the lookup on. </param>
        /// <param name="newPassword"> The new password for the user. </param>
        /// <returns> </returns>
        public bool ResetPassword(string passwordResetToken, string newPassword)
        {
            TUser user = _userStore.GetUserByPasswordResetToken(passwordResetToken);
            if (user == null)
            {
                return false;
            }

            if (String.IsNullOrEmpty(user.Salt))
            {
                user.Salt = _encoder.GenerateSalt();
            }
            user.Password = _encoder.Encode(newPassword, user.Salt);

			//When we set a password we have used the token so we clear it
			user.PasswordResetToken = null;
			user.PasswordResetTokenExpiration = new DateTime(2000,1,1); //DateTime.MinValue is not compatible with sql "datetime" so we simply set a date prior to memflex

            _userStore.Save(user);

            return true;
        }

        #endregion

        #region IFlexOAuthProvider Members

        /// <summary>
        ///   Creates the OAuth account.
        /// </summary>
        /// <param name="provider"> The provider. </param>
        /// <param name="providerUserId"> The provider user id. </param>
        /// <param name="user"> The user. </param>
        public void CreateOAuthAccount(string provider, string providerUserId, TUser user)
        {
            TUser existingUser = _userStore.GetUserByUsername(user.Username, user.License);
            if (existingUser == null)
            {
                _userStore.Add(user);
            }
            _userStore.CreateOAuthAccount(provider, providerUserId, existingUser ?? user);
        }

        /// <summary>
        ///   Dissassociates the OAuth account for a userid.
        /// </summary>
        /// <param name="provider"> The provider. </param>
        /// <param name="providerUserId"> The provider user id. </param>
        /// <returns> </returns>
        public bool DisassociateOAuthAccount(string provider, string providerUserId, string license = null)
        {
            IFlexMembershipUser user = _userStore.GetUserByOAuthProvider(provider, providerUserId);
            if (user == null)
            {
                return false;
            }
            IEnumerable<OAuthAccount> accounts = _userStore.GetOAuthAccountsForUser(user.Username, license);

            if (HasLocalAccount(user.Username, license))
                return _userStore.DeleteOAuthAccount(provider, providerUserId);

            if (accounts.Count() > 1)
                return _userStore.DeleteOAuthAccount(provider, providerUserId);

            return false;
        }

        /// <summary>
        ///   Gets the OAuth client data for a provider
        /// </summary>
        /// <param name="provider"> The provider. </param>
        /// <returns> </returns>
        public AuthenticationClientData GetOAuthClientData(string providerName)
        {
            return _authenticationClients[providerName];
        }

        /// <summary>
        /// Gets the registered client data.
        /// </summary>
        /// <value>
        /// The registered client data.
        /// </value>
        public ICollection<AuthenticationClientData> RegisteredClientData
        {
            get { return _authenticationClients.Values; }
        }

		/// <summary>
		///   Gets the name of the OAuth accounts for a user.
		/// </summary>
		/// <param name="username"> The username. </param>
		/// <param name="license">The license the user belongs to.</param>
		/// <returns> </returns>
		public IEnumerable<OAuthAccount> GetOAuthAccountsFromUserName(string username, string license = null)
        {
            return _userStore.GetOAuthAccountsForUser(username, license);
        }

        /// <summary>
        /// Requests the OAuth authentication.
        /// </summary>
        /// <param name="provider">The provider.</param>
        /// <param name="returnUrl">The return URL.</param>
        public void RequestOAuthAuthentication(string provider, string returnUrl)
        {
            AuthenticationClientData client = _authenticationClients[provider];
            _applicationEnvironment.RequestAuthentication(client.AuthenticationClient, this, returnUrl);
        }

        /// <summary>
        ///   Verifies the OAuth authentication.
        /// </summary>
        /// <param name="action"> The action. </param>
        /// <returns> </returns>
        public AuthenticationResult VerifyOAuthAuthentication(string returnUrl)
        {
            string providerName = _applicationEnvironment.GetOAuthPoviderName();
            if (String.IsNullOrEmpty(providerName))
            {
                return AuthenticationResult.Failed;
            }

            AuthenticationClientData client = _authenticationClients[providerName];
            return _applicationEnvironment.VerifyAuthentication(client.AuthenticationClient, this, returnUrl);
        }

        /// <summary>
        /// Attempts to perform an OAuth Login.
        /// </summary>
        /// <param name="provider">The provider.</param>
        /// <param name="providerUserId">The provider user id.</param>
        /// <param name="persistCookie">if set to <c>true</c> [persist cookie].</param>
        /// <returns></returns>
        public bool OAuthLogin(string provider, string providerUserId, bool persistCookie)
        {
            AuthenticationClientData oauthProvider = _authenticationClients[provider];
            HttpContextBase context = _applicationEnvironment.AcquireContext();
            var securityManager = new OpenAuthSecurityManager(context, oauthProvider.AuthenticationClient, this);
            return securityManager.Login(providerUserId, persistCookie);
        }

        #endregion

        #region IOpenAuthDataProvider Members

        /// <summary>
        /// Gets the user name from open auth.
        /// </summary>
        /// <param name="provider">The provider.</param>
        /// <param name="providerUserId">The provider user id.</param>
        /// <returns></returns>
        public string GetUserNameFromOpenAuth(string provider, string providerUserId)
        {
            IFlexMembershipUser user = _userStore.GetUserByOAuthProvider(provider, providerUserId);
            if (user != null)
            {
                return user.Username;
            }
            return String.Empty;
        }

        #endregion

        /// <summary>
        /// Generates the token.
        /// </summary>
        /// <returns></returns>
        private static string GenerateToken()
        {
            using (var prng = new RNGCryptoServiceProvider())
            {
                return GenerateToken(prng);
            }
        }

        /// <summary>
        /// Generates the token.
        /// </summary>
        /// <param name="generator">The generator.</param>
        /// <returns></returns>
        internal static string GenerateToken(RandomNumberGenerator generator)
        {
            var tokenBytes = new byte[TokenSizeInBytes];
            generator.GetBytes(tokenBytes);
            return HttpServerUtility.UrlTokenEncode(tokenBytes);
        }

        /// <summary>
        /// Registers the client.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="displayName">The display name.</param>
        /// <param name="extraData">The extra data.</param>
        public static void RegisterClient(IAuthenticationClient client,
                                          string displayName, IDictionary<string, object> extraData)
        {
            var clientData = new AuthenticationClientData(client, displayName, extraData);
            _authenticationClients.Add(client.ProviderName, clientData);
        }
    }
}