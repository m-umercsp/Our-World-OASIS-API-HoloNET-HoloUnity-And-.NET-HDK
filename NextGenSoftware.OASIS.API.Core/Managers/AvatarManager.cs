﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NextGenSoftware.OASIS.API.Core.Enums;
using NextGenSoftware.OASIS.API.Core.Events;
using NextGenSoftware.OASIS.API.Core.Helpers;
using NextGenSoftware.OASIS.API.Core.Interfaces;
using NextGenSoftware.OASIS.API.Core.Objects;
using NextGenSoftware.OASIS.API.Core.Security;

namespace NextGenSoftware.OASIS.API.Core.Managers
{
    public class AvatarManager : OASISManager
    {
        private static Dictionary<string, string> _avatarIdToProviderKeyLookup = new Dictionary<string, string>();
        private static Dictionary<string, string> _avatarUsernameToProviderKeyLookup = new Dictionary<string, string>();
        private static Dictionary<string, string> _avatarIdToProviderPrivateKeyLookup = new Dictionary<string, string>();
        private static Dictionary<string, Guid> _providerKeyToAvatarIdLookup = new Dictionary<string, Guid>();
        private static Dictionary<string, IAvatar> _providerKeyToAvatarLookup = new Dictionary<string, IAvatar>();

        public static IAvatar LoggedInAvatar { get; set; }
        private ProviderManagerConfig _config;
        
        public List<IOASISStorage> OASISStorageProviders { get; set; }
        
        public ProviderManagerConfig Config
        {
            get
            {
                if (_config == null)
                    _config = new ProviderManagerConfig();

                return _config;
            }
        }

        public delegate void StorageProviderError(object sender, AvatarManagerErrorEventArgs e);

        //TODO: In future more than one storage provider can be active at a time where each call can specify which provider to use.
        public AvatarManager(IOASISStorage OASISStorageProvider) : base(OASISStorageProvider)
        {

        }

        public IEnumerable<IAvatar> LoadAllAvatarsWithPasswords(ProviderType provider = ProviderType.Default)
        {
            IEnumerable<IAvatar> avatars = ProviderManager.SetAndActivateCurrentStorageProvider(provider).LoadAllAvatars();
            return avatars;
        }

        public IEnumerable<IAvatar> LoadAllAvatars(ProviderType provider = ProviderType.Default)
        {
            IEnumerable<IAvatar> avatars = ProviderManager.SetAndActivateCurrentStorageProvider(provider).LoadAllAvatars();

            foreach (IAvatar avatar in avatars)
                avatar.Password = null;

            return avatars;
        }

        public async Task<IEnumerable<IAvatar>> LoadAllAvatarsAsync(ProviderType provider = ProviderType.Default)
        {
            IEnumerable<IAvatar> avatars = ProviderManager.SetAndActivateCurrentStorageProvider(provider).LoadAllAvatarsAsync().Result;

            foreach (IAvatar avatar in avatars)
                avatar.Password = null;

            return avatars;
        }

        public async Task<IAvatar> LoadAvatarAsync(string providerKey, ProviderType provider = ProviderType.Default)
        {
            IAvatar avatar = ProviderManager.SetAndActivateCurrentStorageProvider(provider).LoadAvatarAsync(providerKey).Result;
            // avatar.Password = null;
            return avatar;
        }

        public async Task<IAvatar> LoadAvatarAsync(Guid id, ProviderType providerType = ProviderType.Default)
        {
            IAvatar avatar = ProviderManager.SetAndActivateCurrentStorageProvider(providerType).LoadAvatarAsync(id).Result;
            // avatar.Password = null;
            return avatar;
        }

        public IAvatar LoadAvatar(Guid id, ProviderType providerType = ProviderType.Default)
        {
            IAvatar avatar = ProviderManager.SetAndActivateCurrentStorageProvider(providerType).LoadAvatar(id);
            //avatar.Password = null;
            return avatar;
        }

        public async Task<IAvatar> LoadAvatarAsync(string username, string password, ProviderType providerType = ProviderType.Default)
        {
            return ProviderManager.SetAndActivateCurrentStorageProvider(providerType).LoadAvatarAsync(username, password).Result;
        }

        public IAvatar LoadAvatar(string username, string password, ProviderType providerType = ProviderType.Default)
        {
            return ProviderManager.SetAndActivateCurrentStorageProvider(providerType).LoadAvatar(username, password);
        }

        //TODO: Replicate Auto-Fail over and Auto-Replication code for all Avatar, HolonManager methods etc...
        public IAvatar LoadAvatar(string username, ProviderType providerType = ProviderType.Default)
        {
            bool needToChangeBack = false;
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;
            IAvatar avatar = null;

            try
            {
                avatar = ProviderManager.SetAndActivateCurrentStorageProvider(providerType).LoadAvatar(username);
            }
            catch (Exception ex)
            {
                avatar = null;
            }

            if (avatar == null)
            {
                // Only try the next provider if they are not set to auto-replicate.
                //   if (ProviderManager.ProvidersThatAreAutoReplicating.Count == 0)
                // {
                foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                {
                    if (type.Value != providerType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                    {
                        try
                        {
                            avatar = ProviderManager.SetAndActivateCurrentStorageProvider(type.Value).LoadAvatar(username);
                            needToChangeBack = true;

                            if (avatar != null)
                                break;
                        }
                        catch (Exception ex2)
                        {
                            avatar = null;
                            //If the next provider errors then just continue to the next provider.
                        }
                    }
                }
            }
            //   }

            // Set the current provider back to the original provider.
            if (needToChangeBack)
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);

            return avatar;
        }

        public async Task<IAvatar> SaveAvatarAsync(IAvatar avatar, ProviderType providerType = ProviderType.Default)
        {
            bool needToChangeBack = false;
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;

            try
            {
                avatar = await ProviderManager.SetAndActivateCurrentStorageProvider(providerType).SaveAvatarAsync(PrepareAvatarForSaving(avatar));
            }
            catch (Exception ex)
            {
                avatar = null;
            }

            if (avatar == null)
            {
                // Only try the next provider if they are not set to auto-replicate.
                //   if (ProviderManager.ProvidersThatAreAutoReplicating.Count == 0)
                // {
                foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                {
                    if (type.Value != providerType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                    {
                        try
                        {
                            avatar = await ProviderManager.SetAndActivateCurrentStorageProvider(type.Value).SaveAvatarAsync(avatar);
                            needToChangeBack = true;

                            if (avatar != null)
                                break;
                        }
                        catch (Exception ex2)
                        {
                            avatar = null;
                            //If the next provider errors then just continue to the next provider.
                        }
                    }
                }
                //   }
            }

            foreach (EnumValue<ProviderType> type in ProviderManager.GetProvidersThatAreAutoReplicating())
            {
                if (type.Value != providerType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                {
                    try
                    {
                        await ProviderManager.SetAndActivateCurrentStorageProvider(type.Value).SaveAvatarAsync(avatar);
                        needToChangeBack = true;
                    }
                    catch (Exception ex)
                    {
                        // Add logging here.
                    }
                }
            }

            // Set the current provider back to the original provider.
          // if (needToChangeBack)
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);

            return avatar;
        }

        public IAvatar SaveAvatar(IAvatar avatar, ProviderType providerType = ProviderType.Default)
        {
            bool needToChangeBack = false;
            ProviderType currentProviderType = ProviderManager.CurrentStorageProviderType.Value;

            try
            {
                avatar = ProviderManager.SetAndActivateCurrentStorageProvider(providerType).SaveAvatar(PrepareAvatarForSaving(avatar));
            }
            catch (Exception ex)
            {
                avatar = null;
            }

            if (avatar == null)
            {
                // Only try the next provider if they are not set to auto-replicate.
              //  if (ProviderManager.ProvidersThatAreAutoReplicating.Count == 0)
              //  {
                    foreach (EnumValue<ProviderType> type in ProviderManager.GetProviderAutoFailOverList())
                    {
                        if (type.Value != providerType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                        {
                            try
                            {
                                avatar = ProviderManager.SetAndActivateCurrentStorageProvider(type.Value).SaveAvatar(avatar);
                                needToChangeBack = true;

                                if (avatar != null)
                                    break;
                            }
                            catch (Exception ex2)
                            {
                                avatar = null;
                                //If the next provider errors then just continue to the next provider.
                            }
                        }
                    }
             //   }
            }


            foreach (EnumValue<ProviderType> type in ProviderManager.GetProvidersThatAreAutoReplicating())
            {
                if (type.Value != providerType && type.Value != ProviderManager.CurrentStorageProviderType.Value)
                {
                    try
                    {
                        ProviderManager.SetAndActivateCurrentStorageProvider(type.Value).SaveAvatar(avatar);
                        needToChangeBack = true;
                    }
                    catch (Exception ex)
                    {
                        // Add logging here.
                    }
                }
            }

            // Set the current provider back to the original provider.
           // if (needToChangeBack)
                ProviderManager.SetAndActivateCurrentStorageProvider(currentProviderType);

            return avatar;
        }


        //TODO: Need to refactor methods below to match the new above ones.
        public bool DeleteAvatar(Guid id, bool softDelete = true, ProviderType providerType = ProviderType.Default)
        {
            return ProviderManager.SetAndActivateCurrentStorageProvider(providerType).DeleteAvatar(id, softDelete);
        }

        public async Task<bool> DeleteAvatarAsync(Guid id, bool softDelete = true, ProviderType providerType = ProviderType.Default)
        {
            return await ProviderManager.SetAndActivateCurrentStorageProvider(providerType).DeleteAvatarAsync(id, softDelete);
        }

        public async Task<KarmaAkashicRecord> AddKarmaToAvatarAsync(IAvatar Avatar, KarmaTypePositive karmaType, KarmaSourceType karmaSourceType, string karamSourceTitle, string karmaSourceDesc, string karmaSourceWebLink = null, ProviderType provider = ProviderType.Default)
        {
            return await ProviderManager.SetAndActivateCurrentStorageProvider(provider).AddKarmaToAvatarAsync(Avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);
        }
        public async Task<KarmaAkashicRecord> AddKarmaToAvatarAsync(Guid avatarId, KarmaTypePositive karmaType, KarmaSourceType karmaSourceType, string karamSourceTitle, string karmaSourceDesc, string karmaSourceWebLink = null, ProviderType provider = ProviderType.Default)
        {
            IAvatar avatar = ProviderManager.SetAndActivateCurrentStorageProvider(provider).LoadAvatar(avatarId);
            return await ProviderManager.CurrentStorageProvider.AddKarmaToAvatarAsync(avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);
        }

        public OASISResult<KarmaAkashicRecord> AddKarmaToAvatar(IAvatar Avatar, KarmaTypePositive karmaType, KarmaSourceType karmaSourceType, string karamSourceTitle, string karmaSourceDesc, string karmaSourceWebLink = null, ProviderType provider = ProviderType.Default)
        {
            return new OASISResult<KarmaAkashicRecord>(ProviderManager.SetAndActivateCurrentStorageProvider(provider).AddKarmaToAvatar(Avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink));
        }
        public OASISResult<KarmaAkashicRecord> AddKarmaToAvatar(Guid avatarId, KarmaTypePositive karmaType, KarmaSourceType karmaSourceType, string karamSourceTitle, string karmaSourceDesc, string karmaSourceWebLink = null, ProviderType provider = ProviderType.Default)
        {
            OASISResult<KarmaAkashicRecord> result = new OASISResult<KarmaAkashicRecord>();
            IAvatar avatar = ProviderManager.SetAndActivateCurrentStorageProvider(provider).LoadAvatar(avatarId);
            
            if (avatar != null)
                result.Result = ProviderManager.CurrentStorageProvider.AddKarmaToAvatar(avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);
            else
            {
                result.IsError = true;
                result.ErrorMessage = "Avatar Not Found";
            }

            return result;
        }

        public async Task<KarmaAkashicRecord> RemoveKarmaFromAvatarAsync(IAvatar Avatar, KarmaTypeNegative karmaType, KarmaSourceType karmaSourceType, string karamSourceTitle, string karmaSourceDesc, string karmaSourceWebLink = null, ProviderType provider = ProviderType.Default)
        {
            return await ProviderManager.SetAndActivateCurrentStorageProvider(provider).RemoveKarmaFromAvatarAsync(Avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);
        }

        public async Task<KarmaAkashicRecord> RemoveKarmaFromAvatarAsync(Guid avatarId, KarmaTypeNegative karmaType, KarmaSourceType karmaSourceType, string karamSourceTitle, string karmaSourceDesc, string karmaSourceWebLink = null, ProviderType provider = ProviderType.Default)
        {
            IAvatar avatar = ProviderManager.SetAndActivateCurrentStorageProvider(provider).LoadAvatar(avatarId);
            return await ProviderManager.CurrentStorageProvider.RemoveKarmaFromAvatarAsync(avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);
        }

        public KarmaAkashicRecord RemoveKarmaFromAvatar(IAvatar Avatar, KarmaTypeNegative karmaType, KarmaSourceType karmaSourceType, string karamSourceTitle, string karmaSourceDesc, string karmaSourceWebLink = null, ProviderType provider = ProviderType.Default)
        {
            return ProviderManager.SetAndActivateCurrentStorageProvider(provider).RemoveKarmaFromAvatar(Avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);
        }

        public OASISResult<KarmaAkashicRecord> RemoveKarmaFromAvatar(Guid avatarId, KarmaTypeNegative karmaType, KarmaSourceType karmaSourceType, string karamSourceTitle, string karmaSourceDesc, string karmaSourceWebLink = null, ProviderType provider = ProviderType.Default)
        {
            OASISResult<KarmaAkashicRecord> result = new OASISResult<KarmaAkashicRecord>();
            IAvatar avatar = ProviderManager.SetAndActivateCurrentStorageProvider(provider).LoadAvatar(avatarId);

            if (avatar != null)
                result.Result = ProviderManager.CurrentStorageProvider.RemoveKarmaFromAvatar(avatar, karmaType, karmaSourceType, karamSourceTitle, karmaSourceDesc, karmaSourceWebLink);
            else
            {
                result.IsError = true;
                result.ErrorMessage = "Avatar Not Found";
            }

            return result;
        }

        // Could be used as the public key for private/public key pairs. Could also be a username/accountname/unique id/etc, etc.
        public IAvatar LinkProviderKeyToAvatar(Guid avatarId, ProviderType providerTypeToLinkTo, string providerKey, ProviderType providerToLoadAvatarFrom = ProviderType.Default)
        {
            IAvatar avatar = ProviderManager.SetAndActivateCurrentStorageProvider(providerToLoadAvatarFrom).LoadAvatar(avatarId);
            avatar.ProviderKey[providerTypeToLinkTo] = providerKey;
            avatar = avatar.Save();
            return avatar;
        }

        // Private key for a public/private keypair.
        public IAvatar LinkProviderPrivateKeyToAvatar(Guid avatarId, ProviderType providerTypeToLinkTo, string providerPrivateKey, ProviderType providerToLoadAvatarFrom = ProviderType.Default)
        {
            IAvatar avatar = ProviderManager.SetAndActivateCurrentStorageProvider(providerToLoadAvatarFrom).LoadAvatar(avatarId);
            avatar.ProviderPrivateKey[providerTypeToLinkTo] = StringCipher.Encrypt(providerPrivateKey);
            avatar = avatar.Save();
            return avatar;
        }

        public string GetProviderKeyForAvatar(Guid avatarId, ProviderType providerType)
        {
            string key = string.Concat(Enum.GetName(providerType), avatarId);

            if (!_avatarIdToProviderKeyLookup.ContainsKey(key))
            {
                IAvatar avatar = LoadAvatar(avatarId);
                GetProviderKeyForAvatar(avatar, providerType, key, _avatarIdToProviderKeyLookup);
            }

            return _avatarIdToProviderKeyLookup[key];
        }

        public string GetProviderKeyForAvatar(string avatarUsername, ProviderType providerType)
        {
            string key = string.Concat(Enum.GetName(providerType), avatarUsername);

            if (!_avatarUsernameToProviderKeyLookup.ContainsKey(key))
            {
                IAvatar avatar = LoadAvatar(avatarUsername);
                GetProviderKeyForAvatar(avatar, providerType, key, _avatarUsernameToProviderKeyLookup);
            }

            return _avatarUsernameToProviderKeyLookup[key];
        }

        //TODO: COME BACK TO THIS! EVENTUALLY NEED TO MAKE ALL AVATAR FUNCTIONS ACCEPT EITHER AVATAR ID OR AVATAR USERNAME...
        private string GetProviderKeyForAvatar(IAvatar avatar, ProviderType providerType, string key, Dictionary<string, string> dictionaryCache)
        {
            if (avatar != null)
            {
                if (avatar.ProviderKey.ContainsKey(providerType))
                    dictionaryCache[key] = avatar.ProviderKey[providerType];
                else
                    throw new InvalidOperationException(string.Concat("The avatar with id ", avatar.Id, " and username ", avatar.Username, " was not been linked to the ", Enum.GetName(providerType), " provider. Please use the LinkProviderKeyToAvatar method on the AvatarManager or avatar REST API."));
            }
            else
                throw new InvalidOperationException(string.Concat("The avatar with id ", avatar.Id, " and username ", avatar.Username, " was not found."));

            return dictionaryCache[key];
        }

        public string GetPrivateProviderKeyForAvatar(Guid avatarId, ProviderType providerType)
        {
            string key = string.Concat(Enum.GetName(providerType), avatarId);

            if (LoggedInAvatar.Id != avatarId)
                throw new InvalidOperationException("You cannot retreive the private key for another person's avatar. Please login to this account and try again.");

            if (!_avatarIdToProviderPrivateKeyLookup.ContainsKey(key))
            {
                IAvatar avatar = LoadAvatar(avatarId);

                if (avatar != null)
                {
                    if (avatar.ProviderPrivateKey.ContainsKey(providerType))
                        _avatarIdToProviderPrivateKeyLookup[key] = avatar.ProviderPrivateKey[providerType];
                    else
                        throw new InvalidOperationException(string.Concat("The avatar with id ", avatarId, " has not been linked to the ", Enum.GetName(providerType), " provider. Please use the LinkProviderPrivateKeyToAvatar method on the AvatarManager or avatar REST API."));
                }
                else
                    throw new InvalidOperationException(string.Concat("The avatar with id ", avatarId, " was not found."));
            }

            return StringCipher.Decrypt(_avatarIdToProviderPrivateKeyLookup[key]);
        }

        public Guid GetAvatarIdForProviderKey(string providerKey, ProviderType providerType)
        {
            // TODO: Do we need to store both the id and whole avatar in the cache? Think only need one? Just storing the id would use less memory and be faster but there may be use cases for when we need the whole avatar?
            // In future, if there is not a use case for the whole avatar we will just use the id cache and remove the other.

            string key = string.Concat(Enum.GetName(providerType), providerKey);

            if (!_providerKeyToAvatarIdLookup.ContainsKey(key))
                _providerKeyToAvatarIdLookup[key] = GetAvatarForProviderKey(providerKey, providerType).Id;

            return _providerKeyToAvatarIdLookup[key];
        }

        //public string GetAvatarUsernameForProviderKey(string providerKey, ProviderType providerType)
        //{
        //    // TODO: Do we need to store both the id and whole avatar in the cache? Think only need one? Just storing the id would use less memory and be faster but there may be use cases for when we need the whole avatar?
        //    // In future, if there is not a use case for the whole avatar we will just use the id cache and remove the other.

        //    string key = string.Concat(Enum.GetName(providerType), providerKey);

        //    if (!_providerKeyToAvatarIdLookup.ContainsKey(key))
        //        _providerKeyToAvatarIdLookup[key] = GetAvatarForProviderKey(providerKey, providerType).Id;

        //    return _providerKeyToAvatarIdLookup[key];
        //}

        //TODO: Think will remove this if there is no good use case for it?
        public IAvatar GetAvatarForProviderKey(string providerKey, ProviderType providerType)
        {
            string key = string.Concat(Enum.GetName(providerType), providerKey);

            if (!_providerKeyToAvatarLookup.ContainsKey(key))
            {
                IAvatar avatar = LoadAllAvatars().FirstOrDefault(x => x.ProviderKey.ContainsKey(providerType) && x.ProviderKey[providerType] == providerKey);

                if (avatar != null)
                    _providerKeyToAvatarIdLookup[key] = avatar.Id;
                else
                    throw new InvalidOperationException(string.Concat("The provider Key ", providerKey, " for the ", Enum.GetName(providerType), " providerType has not been linked to an avatar. Please use the LinkProviderKeyToAvatar method on the AvatarManager or avatar REST API."));
            }

            return _providerKeyToAvatarLookup[key];
        }

        public OASISResult<Dictionary<ProviderType, string>> GetAllProviderKeysForAvatar(Guid avatarId)
        {
            OASISResult<Dictionary<ProviderType, string>> result = new OASISResult<Dictionary<ProviderType, string>>();
            IAvatar avatar = LoadAvatar(avatarId);

            if (avatar != null)
                result.Result = avatar.ProviderKey;
            else
            {
                result.IsError = true;
                result.ErrorMessage = string.Concat("No avatar was found for the id ", avatarId);
                //throw new InvalidOperationException(string.Concat("No avatar was found for the id ", avatarId));
                // NOTE: Would rather return OASISResult's rather than throw exceptions because less overhead (exceptions return a full stack trace).
                // TODO: Eventually need OASISResult's implemented for ALL OASIS functions (this includes replacing all exceptions where possible).
            }

            return result;
        }

        public OASISResult<Dictionary<ProviderType, string>> GetAllPrivateProviderKeysForAvatar(Guid avatarId)
        {
            OASISResult<Dictionary<ProviderType, string>> result = new OASISResult<Dictionary<ProviderType, string>>();

            if (LoggedInAvatar.Id != avatarId)
            {
                result.IsError = true;
                result.ErrorMessage = "ERROR: You can only retreive your own private keys, not another persons avatar.";
            }
            else
            {
                IAvatar avatar = LoadAvatar(avatarId);

                if (avatar != null)
                {
                    result.Result = avatar.ProviderPrivateKey;

                    // Decrypt the keys only for this return object (there are not stored in memory or storage unenrypted).
                    foreach (ProviderType providerType in result.Result.Keys)
                        result.Result[providerType] = StringCipher.Decrypt(result.Result[providerType]);
                }
                else
                {
                    result.IsError = true;
                    result.ErrorMessage = string.Concat("No avatar was found for the id ", avatarId);
                    //throw new InvalidOperationException(string.Concat("No avatar was found for the id ", avatarId));
                    // NOTE: Would rather return OASISResult's rather than throw exceptions because less overhead (exceptions return a full stack trace).
                    // TODO: Eventually need OASISResult's implemented for ALL OASIS functions (this includes replacing all exceptions where possible).
                }
            }

            return result;
        }

        private IAvatar PrepareAvatarForSaving(IAvatar avatar)
        {
            if (string.IsNullOrEmpty(avatar.Username))
                avatar.Username = avatar.Email;

            // TODO: I think it's best to include audit stuff here so the providers do not need to worry about it?
            // Providers could always override this behaviour if they choose...
            if (avatar.Id != Guid.Empty)
            {
                avatar.ModifiedDate = DateTime.Now;

                if (LoggedInAvatar != null)
                    avatar.ModifiedByAvatarId = LoggedInAvatar.Id;
            }
            else
            {
                avatar.IsActive = true;
                avatar.CreatedDate = DateTime.Now;

                if (LoggedInAvatar != null)
                    avatar.CreatedByAvatarId = LoggedInAvatar.Id;
            }

            return avatar;
        }
    }
}
