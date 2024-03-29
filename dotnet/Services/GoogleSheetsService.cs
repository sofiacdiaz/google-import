﻿using SheetsCatalogImport.Data;
using SheetsCatalogImport.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Vtex.Api.Context;

namespace SheetsCatalogImport.Services
{
    public class GoogleSheetsService : IGoogleSheetsService
    {
        private readonly IIOServiceContext _context;
        private readonly IVtexEnvironmentVariableProvider _environmentVariableProvider;
        private readonly ISheetsCatalogImportRepository _sheetsCatalogImportRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IHttpClientFactory _clientFactory;
        private readonly string _applicationName;

        public GoogleSheetsService(IIOServiceContext context, IVtexEnvironmentVariableProvider environmentVariableProvider, ISheetsCatalogImportRepository sheetsCatalogImportRepository, IHttpContextAccessor httpContextAccessor, IHttpClientFactory clientFactory)
        {
            this._context = context ??
                            throw new ArgumentNullException(nameof(context));

            this._environmentVariableProvider = environmentVariableProvider ??
                                                throw new ArgumentNullException(nameof(environmentVariableProvider));

            this._sheetsCatalogImportRepository = sheetsCatalogImportRepository ??
                                                throw new ArgumentNullException(nameof(sheetsCatalogImportRepository));

            this._httpContextAccessor = httpContextAccessor ??
                                        throw new ArgumentNullException(nameof(httpContextAccessor));

            this._clientFactory = clientFactory ??
                               throw new ArgumentNullException(nameof(clientFactory));

            this._applicationName =
                $"{this._environmentVariableProvider.ApplicationVendor}.{this._environmentVariableProvider.ApplicationName}";
        }

        public async Task<Token> RefreshGoogleAuthorizationToken(string refreshToken)
        {
            Token token = await this.RefreshToken(refreshToken);
            return token;
        }

        public async Task<bool> RevokeGoogleAuthorizationToken()
        {
            bool success = false;

            Token token = await _sheetsCatalogImportRepository.LoadToken();

            if (token != null && string.IsNullOrEmpty(token.AccessToken))
            {
                _context.Vtex.Logger.Info("RevokeGoogleAuthorizationToken", null, "Token Empty");
                await _sheetsCatalogImportRepository.SaveToken(new Token());
                await this.ShareToken(new Token());
                success = true;
            }
            else
            {
                success = await this.RevokeGoogleAuthorizationToken(token);
                if(success)
                {
                    await _sheetsCatalogImportRepository.SaveToken(new Token());
                    await this.ShareToken(new Token());
                }
            }

            return success;
        }

        public async Task<string> GetAuthUrl()
        {
            string authUrl = string.Empty;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://{SheetsCatalogImportConstants.AUTH_SITE_BASE}/{SheetsCatalogImportConstants.AUTH_APP_PATH}/{SheetsCatalogImportConstants.AUTH_PATH}/{SheetsCatalogImportConstants.APP_TYPE}")
                };

                request.Headers.Add(SheetsCatalogImportConstants.USE_HTTPS_HEADER_NAME, "true");
                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    authUrl = responseContent;
                    string siteUrl = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.FORWARDED_HOST];
                    authUrl = authUrl.Replace("state=", $"state={siteUrl}");
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetAuthUrl", null, $"Failed to get auth url [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("GetAuthUrl", null, $"Error getting auth url", ex);
            }

            return authUrl;
        }

        private async Task<bool> RevokeGoogleAuthorizationToken(Token token)
        {
            bool success = false;

            if (token != null && string.IsNullOrEmpty(token.AccessToken))
            {
                _context.Vtex.Logger.Info("RevokeGoogleAuthorizationToken", null, "Token Empty");
                success = true;
            }
            else
            {
                string jsonSerializedData = JsonConvert.SerializeObject(token);
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri($"http://{SheetsCatalogImportConstants.AUTH_SITE_BASE}/{SheetsCatalogImportConstants.AUTH_APP_PATH}/{SheetsCatalogImportConstants.REVOKE_PATH}"),
                    Content = new StringContent(jsonSerializedData, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_FORM)
                };

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                try
                {
                    var response = await client.SendAsync(request);
                    string responseContent = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        success = true;
                    }
                    else
                    {
                        _context.Vtex.Logger.Info("RevokeGoogleAuthorizationToken", null, $"{response.StatusCode} {responseContent}");
                    }
                }
                catch (Exception ex)
                {
                    _context.Vtex.Logger.Error("RevokeGoogleAuthorizationToken", null, $"Have Access Token? {!string.IsNullOrEmpty(token.AccessToken)} Have Refresh Token?{!string.IsNullOrEmpty(token.RefreshToken)}", ex);
                }
            }

            return success;
        }

        private async Task<Token> RefreshToken(string refreshToken)
        {
            Token token = null;

            if (!string.IsNullOrEmpty(refreshToken))
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri($"http://{SheetsCatalogImportConstants.AUTH_SITE_BASE}/{SheetsCatalogImportConstants.AUTH_APP_PATH}/{SheetsCatalogImportConstants.REFRESH_PATH}/{SheetsCatalogImportConstants.APP_TYPE}/{HttpUtility.UrlEncode(refreshToken)}"),
                    Content = new StringContent(string.Empty, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_FORM)
                };

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                try
                {
                    var response = await client.SendAsync(request);
                    string responseContent = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        token = JsonConvert.DeserializeObject<Token>(responseContent);
                    }
                    else
                    {
                        _context.Vtex.Logger.Info("RefreshToken", null, $"{response.StatusCode} {responseContent}");
                    }
                }
                catch (Exception ex)
                {
                    _context.Vtex.Logger.Error("RefreshToken", null, $"Refresh Token {refreshToken}", ex);
                }
            }

            return token;
        }

        public async Task<bool> SaveToken(Token token)
        {
            return await _sheetsCatalogImportRepository.SaveToken(token);
        }

        public async Task<Token> GetGoogleToken()
        {
            Token token = await _sheetsCatalogImportRepository.LoadToken();
            if (token != null && !string.IsNullOrEmpty(token.RefreshToken))
            {
                string refreshToken = token.RefreshToken;
                if (token.ExpiresAt <= DateTime.Now)
                {
                    token = await this.RefreshGoogleAuthorizationToken(refreshToken);
                    if (token != null)
                    {
                        token.ExpiresAt = DateTime.Now.AddSeconds(token.ExpiresIn);
                        if (string.IsNullOrEmpty(token.RefreshToken))
                        {
                            token.RefreshToken = refreshToken;
                        }

                        this.ShareToken(token);
                        await _sheetsCatalogImportRepository.SaveToken(token);
                    }
                    else
                    {
                        await RevokeGoogleAuthorizationToken();
                        await _sheetsCatalogImportRepository.ClearImportLock();
                        _context.Vtex.Logger.Warn("GetGoogleToken", null, $"Could not refresh token.");
                    }
                }
            }
            else
            {
                _context.Vtex.Logger.Warn("GetGoogleToken", null, $"Could not load token.  Refresh token was null. Have Access token?{token != null && !string.IsNullOrEmpty(token.AccessToken)}");
                await this.RevokeGoogleAuthorizationToken();
                await _sheetsCatalogImportRepository.ClearImportLock();
            }

            return token;
        }

        public async Task<ListFilesResponse> ListSheetsInFolder(string folderId)
        {
            ListFilesResponse listFilesResponse = null;
            string responseContent = string.Empty;
            Token token = await this.GetGoogleToken();
            if (token != null && !string.IsNullOrEmpty(token.AccessToken))
            {
                string fields = "*";
                string query = $"mimeType contains 'spreadsheet' and trashed = false and '{folderId}' in parents ";

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"{SheetsCatalogImportConstants.GOOGLE_DRIVE_URL}/{SheetsCatalogImportConstants.GOOGLE_DRIVE_FILES}?fields={fields}&q={query}&pageSize={SheetsCatalogImportConstants.GOOGLE_DRIVE_PAGE_SIZE}"),
                    Content = new StringContent(string.Empty, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, $"{token.TokenType} {token.AccessToken}");

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                try
                {
                    var response = await client.SendAsync(request);
                    responseContent = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        listFilesResponse = JsonConvert.DeserializeObject<ListFilesResponse>(responseContent);
                        _context.Vtex.Logger.Info("ListSheetsInFolder", folderId, $"{listFilesResponse.Files.Count} files.  Complete list? {!listFilesResponse.IncompleteSearch}");
                    }
                    else
                    {
                        _context.Vtex.Logger.Warn("ListSheetsInFolder", folderId, $"[{response.StatusCode}] {responseContent}");
                    }
                }
                catch (Exception ex)
                {
                    _context.Vtex.Logger.Error("ListSheetsInFolder", folderId, $"Error", ex);
                }
            }
            else
            {
                _context.Vtex.Logger.Warn("ListSheetsInFolder", folderId, "Token error.");
            }

            return listFilesResponse;
        }

        public async Task<Dictionary<string, string>> ListFolders(string parentId = null)
        {
            Dictionary<string, string> folders = null;
            string responseContent = string.Empty;
            Token token = await this.GetGoogleToken();
            if (token != null && !string.IsNullOrEmpty(token.AccessToken))
            {
                string fields = "*";
                string query = "mimeType = 'application/vnd.google-apps.folder' and trashed = false";
                if (!string.IsNullOrEmpty(parentId))
                {
                    query = $"{query} and '{parentId}' in parents";
                }

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"{SheetsCatalogImportConstants.GOOGLE_DRIVE_URL}/{SheetsCatalogImportConstants.GOOGLE_DRIVE_FILES}?fields={fields}&q={query}&pageSize={SheetsCatalogImportConstants.GOOGLE_DRIVE_PAGE_SIZE}"),
                    Content = new StringContent(string.Empty, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, $"{token.TokenType} {token.AccessToken}");

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                try
                {
                    var response = await client.SendAsync(request);
                    responseContent = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        folders = new Dictionary<string, string>();
                        ListFilesResponse listFilesResponse = JsonConvert.DeserializeObject<ListFilesResponse>(responseContent);
                        foreach (GoogleFile folder in listFilesResponse.Files)
                        {
                            folders.Add(folder.Id, folder.Name);
                        }
                    }
                    else
                    {
                        _context.Vtex.Logger.Info("ListFolders", parentId, $"[{response.StatusCode}] {responseContent}");
                    }
                }
                catch(Exception ex)
                {
                    _context.Vtex.Logger.Error("ListFolders", parentId, $"List folders error. {parentId}", ex);
                }
            }
            else
            {
                _context.Vtex.Logger.Info("ListFolders", parentId, "Token error.");
            }

            return folders;
        }

        public async Task<ListFilesResponse> GetFolders()
        {
            ListFilesResponse listFilesResponse = null;
            string responseContent = string.Empty;
            Token token = await this.GetGoogleToken();
            if (token != null && !string.IsNullOrEmpty(token.AccessToken))
            {
                string fields = "*";
                string query = "mimeType = 'application/vnd.google-apps.folder' and trashed = false";
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"{SheetsCatalogImportConstants.GOOGLE_DRIVE_URL}/{SheetsCatalogImportConstants.GOOGLE_DRIVE_FILES}?fields={fields}&q={query}&pageSize={SheetsCatalogImportConstants.GOOGLE_DRIVE_PAGE_SIZE}"),
                    Content = new StringContent(string.Empty, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, $"{token.TokenType} {token.AccessToken}");

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                try
                {
                    var response = await client.SendAsync(request);
                    responseContent = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        listFilesResponse = JsonConvert.DeserializeObject<ListFilesResponse>(responseContent);
                    }
                    else
                    {
                        _context.Vtex.Logger.Info("GetFolders", null, $"[{response.StatusCode}] {responseContent}");
                    }
                }
                catch (Exception ex)
                {
                    _context.Vtex.Logger.Error("GetFolders", null, $"List folders error.", ex);
                }
            }
            else
            {
                _context.Vtex.Logger.Info("GetFolders", null, "Token error.");
            }

            return listFilesResponse;
        }

        public async Task<string> CreateFolder(string folderName, string parentId = null)
        {
            string folderId = string.Empty;
            string responseContent = string.Empty;
            Token token = await this.GetGoogleToken();
            if (token != null && !string.IsNullOrEmpty(token.AccessToken))
            {
                dynamic metadata = new JObject();
                metadata.name = folderName;
                metadata.mimeType = "application/vnd.google-apps.folder";
                if (!string.IsNullOrEmpty(parentId))
                {
                    JArray jarrayObj = new JArray();
                    jarrayObj.Add(parentId);
                    metadata.parents = jarrayObj;
                }

                var jsonSerializedMetadata = JsonConvert.SerializeObject(metadata);

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri($"{SheetsCatalogImportConstants.GOOGLE_DRIVE_URL}/{SheetsCatalogImportConstants.GOOGLE_DRIVE_FILES}"),
                    Content = new StringContent(jsonSerializedMetadata, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, $"{token.TokenType} {token.AccessToken}");

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                try
                {
                    var response = await client.SendAsync(request);
                    responseContent = await response.Content.ReadAsStringAsync();
                    _context.Vtex.Logger.Info("CreateFolder", folderName, $"[{response.StatusCode}] {responseContent}");
                    if (response.IsSuccessStatusCode)
                    {
                        CreateFolderResponse createFolderResponse = JsonConvert.DeserializeObject<CreateFolderResponse>(responseContent);
                        folderId = createFolderResponse.Id;
                    }
                }
                catch(Exception ex)
                {
                    _context.Vtex.Logger.Error("CreateFolder", folderName, $"Error. ParentId?{parentId}", ex);
                }
            }
            else
            {
                _context.Vtex.Logger.Error("CreateFolder", folderName, "Token error.");
            }

            return folderId;
        }

        public async Task<bool> MoveFile(string fileId, string folderId)
        {
            bool success = false;
            string responseContent = string.Empty;
            if (!string.IsNullOrEmpty(fileId) && !string.IsNullOrEmpty(folderId))
            {
                Token token = await this.GetGoogleToken();
                if (token != null && !string.IsNullOrEmpty(token.AccessToken))
                {
                    dynamic metadata = new JObject();
                    metadata.id = folderId;

                    var jsonSerializedMetadata = JsonConvert.SerializeObject(metadata);

                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Post,
                        RequestUri = new Uri($"{SheetsCatalogImportConstants.GOOGLE_DRIVE_URL_V2}/{SheetsCatalogImportConstants.GOOGLE_DRIVE_FILES}/{fileId}/parents?enforceSingleParent=true"), // fields=*&q={query}
                        Content = new StringContent(jsonSerializedMetadata, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                    };

                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, $"{token.TokenType} {token.AccessToken}");

                    string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                    if (authToken != null)
                    {
                        request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    }

                    var client = _clientFactory.CreateClient();
                    try
                    {
                        var response = await client.SendAsync(request);
                        responseContent = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            _context.Vtex.Logger.Info("MoveFile", null, $"[{response.StatusCode}] {responseContent}");
                        }

                        success = response.IsSuccessStatusCode;
                    }
                    catch (Exception ex)
                    {
                        _context.Vtex.Logger.Error("MoveFile", folderId, $"FileId {fileId}", ex);
                    }
                }
                else
                {
                    _context.Vtex.Logger.Warn("MoveFile", null, "Token error.");
                }
            }
            else
            {
                _context.Vtex.Logger.Warn("MoveFile", null, "Parameter missing.");
            }

            return success;
        }

        public async Task<string> GetSheet(string fileId, string range)
        {
            bool success = false;
            string responseContent = string.Empty;
            if (string.IsNullOrEmpty(range))
            {
                range = "A:AZ";
            }

            if (!string.IsNullOrEmpty(fileId))
            {
                Token token = await this.GetGoogleToken();
                if (token != null && !string.IsNullOrEmpty(token.AccessToken))
                {
                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Get,
                        RequestUri = new Uri($"{SheetsCatalogImportConstants.GOOGLE_SHEET_URL}/{SheetsCatalogImportConstants.GOOGLE_DRIVE_SHEETS}/{fileId}/values:batchGet?ranges={range}")
                    };

                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, $"{token.TokenType} {token.AccessToken}");

                    string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                    if (authToken != null)
                    {
                        request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    }

                    var client = _clientFactory.CreateClient();
                    try
                    {
                        var response = await client.SendAsync(request);
                        responseContent = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            _context.Vtex.Logger.Info("GetSheet", null, $"FileId:{fileId} [{response.StatusCode}] '{responseContent}'");
                        }

                        success = response.IsSuccessStatusCode;
                    }
                    catch (Exception ex)
                    {
                        _context.Vtex.Logger.Error("GetSheet", null, $"FileId {fileId}", ex);
                    }
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetSheet", null, "Token error.");
                }
            }
            else
            {
                _context.Vtex.Logger.Warn("GetSheet", null, "Parameter missing.");
            }

            return responseContent;
        }

        public async Task<bool> SetPermission(string fileId)
        {
            bool success = false;
            string responseContent = string.Empty;
            if (!string.IsNullOrEmpty(fileId))
            {
                Token token = await this.GetGoogleToken();
                if (token != null && !string.IsNullOrEmpty(token.AccessToken))
                {
                    dynamic metadata = new JObject();
                    metadata.type = "anyone";
                    metadata.role = "reader";

                    var jsonSerializedMetadata = JsonConvert.SerializeObject(metadata);

                    // POST http://www.googleapis.com/drive/v3/files/fileId/permissions
                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Post,
                        RequestUri = new Uri($"{SheetsCatalogImportConstants.GOOGLE_DRIVE_URL}/{SheetsCatalogImportConstants.GOOGLE_DRIVE_FILES}/{fileId}/permissions"),
                        Content = new StringContent(jsonSerializedMetadata, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                    };

                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, $"{token.TokenType} {token.AccessToken}");

                    string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                    if (authToken != null)
                    {
                        request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    }

                    var client = _clientFactory.CreateClient();
                    try
                    {
                        var response = await client.SendAsync(request);
                        responseContent = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            _context.Vtex.Logger.Info("SetPermission", null, $"[{response.StatusCode}] {responseContent}");
                        }

                        success = response.IsSuccessStatusCode;
                    }
                    catch (Exception ex)
                    {
                        _context.Vtex.Logger.Error("SetPermission", null, $"FileId {fileId}", ex);
                    }
                }
                else
                {
                    _context.Vtex.Logger.Warn("SetPermission", null, "Token error.");
                }
            }
            else
            {
                _context.Vtex.Logger.Warn("MoveFile", null, "Parameter missing.");
            }

            return success;
        }

        public async Task<bool> RenameFile(string fileId, string fileName)
        {
            bool success = false;
            string responseContent = string.Empty;
            if (!string.IsNullOrEmpty(fileId) && !string.IsNullOrEmpty(fileName))
            {
                Token token = await this.GetGoogleToken();
                if (token != null && !string.IsNullOrEmpty(token.AccessToken))
                {
                    dynamic metadata = new JObject();
                    metadata.title = fileName;

                    var jsonSerializedMetadata = JsonConvert.SerializeObject(metadata);

                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Patch,
                        RequestUri = new Uri($"{SheetsCatalogImportConstants.GOOGLE_DRIVE_URL_V2}/{SheetsCatalogImportConstants.GOOGLE_DRIVE_FILES}/{fileId}"),
                        Content = new StringContent(jsonSerializedMetadata, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                    };

                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, $"{token.TokenType} {token.AccessToken}");

                    string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                    if (authToken != null)
                    {
                        request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    }

                    var client = _clientFactory.CreateClient();
                    try
                    {
                        var response = await client.SendAsync(request);
                        responseContent = await response.Content.ReadAsStringAsync();

                        success = response.IsSuccessStatusCode;
                    }
                    catch (Exception ex)
                    {
                        _context.Vtex.Logger.Error("RenameFile", null, $"FileId {fileId}, Filename '{fileName}'", ex);
                    }
                }
                else
                {
                    _context.Vtex.Logger.Warn("RenameFile", null, "Token error.");
                }
            }
            else
            {
                _context.Vtex.Logger.Warn("RenameFile", null, "Parameter missing.");
            }

            return success;
        }

        public async Task<string> SaveFile(StringBuilder file)
        {
            string fileId = string.Empty;
            CreateFolderResponse createResponse = null;
            string responseContent = string.Empty;
            if (file != null)
            {
                Token token = await this.GetGoogleToken();
                if (token != null && !string.IsNullOrEmpty(token.AccessToken))
                {
                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Post,
                        RequestUri = new Uri($"{SheetsCatalogImportConstants.GOOGLE_DRIVE_UPLOAD_URL}/{SheetsCatalogImportConstants.GOOGLE_DRIVE_FILES}?uploadType=media&supportsAllDrives=true"),
                        Content = new StringContent(file.ToString(), Encoding.UTF8, SheetsCatalogImportConstants.TEXT)
                    };

                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, $"{token.TokenType} {token.AccessToken}");

                    string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                    if (authToken != null)
                    {
                        request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    }

                    var client = _clientFactory.CreateClient();
                    try
                    {
                        var response = await client.SendAsync(request);
                        responseContent = await response.Content.ReadAsStringAsync();

                        if(response.IsSuccessStatusCode)
                        {
                            createResponse = JsonConvert.DeserializeObject<CreateFolderResponse>(responseContent);
                            fileId = createResponse.Id;
                        }
                    }
                    catch (Exception ex)
                    {
                        _context.Vtex.Logger.Error("SaveFile", null, "Error saving file", ex);
                    }
                }
                else
                {
                    _context.Vtex.Logger.Warn("SaveFile", null, "Token error.");
                }
            }
            else
            {
                _context.Vtex.Logger.Warn("SaveFile", null, "Parameter missing.");
            }

            return fileId;
        }

        public async Task<string> CreateSheet()
        {
            string sheetName = SheetsCatalogImportConstants.SheetNames.SHEET_NAME;
            string sheetLabel = SheetsCatalogImportConstants.SheetNames.PRODUCTS;
            string instructionsLabel = SheetsCatalogImportConstants.SheetNames.INSTRUCTIONS;
            string imagesLabel = SheetsCatalogImportConstants.SheetNames.IMAGES;
            string validationLabel = SheetsCatalogImportConstants.SheetNames.VALIDATION;
            string[] headerRowLabels = SheetsCatalogImportConstants.HEADER.Split(',').Select(str => str.Trim()).ToArray();

            int headerIndex = 0;
            Dictionary<string, int> headerIndexDictionary = new Dictionary<string, int>();
            foreach (string header in headerRowLabels)
            {
                headerIndexDictionary.Add(header.ToLower(), headerIndex);
                headerIndex++;
            }

            int statusRow = headerIndexDictionary["status"];
            int displayIfOutOfStockRow = headerIndexDictionary["display if out of stock"];
            int updateRow = headerIndexDictionary["update"];
            int activateSkuRow = headerIndexDictionary["activate sku"];

            GoogleSheetCreate googleSheetCreate = new GoogleSheetCreate
            {
                Properties = new GoogleSheetProperties
                {
                    Title = sheetName
                },
                Sheets = new Sheet[]
                {
                    new Sheet
                    {
                        Properties = new SheetProperties
                        {
                            SheetId = 0,
                            Title = sheetLabel,
                            Index = 0,
                            GridProperties = new GridProperties
                            {
                                ColumnCount = headerRowLabels.Count(),
                                RowCount = SheetsCatalogImportConstants.DEFAULT_SHEET_SIZE
                            },
                            SheetType = "GRID"
                        },
                        ConditionalFormats = new ConditionalFormat[]
                        {
                            new ConditionalFormat
                            {
                                BooleanRule = new BooleanRule
                                {
                                    Condition = new Condition
                                    {
                                        Type = "TEXT_CONTAINS",
                                        Values = new Value[]
                                        {
                                            new Value
                                            {
                                                UserEnteredValue = "Error"
                                            }
                                        }
                                    },
                                    Format = new Format
                                    {
                                        BackgroundColor = new BackgroundColorClass
                                        {
                                            Blue = 0.6,
                                            Green = 0.6,
                                            Red = 0.91764706
                                        },
                                        BackgroundColorStyle = new BackgroundColorStyle
                                        {
                                            RgbColor = new BackgroundColorClass
                                            {
                                                Blue = 0.6,
                                                Green = 0.6,
                                                Red = 0.91764706
                                            }
                                        },
                                        TextFormat = new FormatTextFormat
                                        {
                                            ForegroundColor = new BackgroundColorClass(),
                                            ForegroundColorStyle = new BackgroundColorStyle
                                            {
                                                RgbColor = new BackgroundColorClass()
                                            }
                                        }
                                    }
                                },
                                Ranges = new CreateRange[]
                                {
                                    new CreateRange
                                    {
                                        EndColumnIndex = statusRow + 1,
                                        EndRowIndex = SheetsCatalogImportConstants.DEFAULT_SHEET_SIZE,
                                        StartColumnIndex = statusRow,
                                        StartRowIndex = 1
                                    }
                                }
                            },
                            new ConditionalFormat
                            {
                                BooleanRule = new BooleanRule
                                {
                                    Condition = new Condition
                                    {
                                        Type = "TEXT_CONTAINS",
                                        Values = new Value[]
                                        {
                                            new Value
                                            {
                                                UserEnteredValue = "Done"
                                            }
                                        }
                                    },
                                    Format = new Format
                                    {
                                        BackgroundColor = new BackgroundColorClass
                                        {
                                            Blue = 0.8039216,
                                            Green = 0.88235295,
                                            Red = 0.7176471
                                        },
                                        BackgroundColorStyle = new BackgroundColorStyle
                                        {
                                            RgbColor = new BackgroundColorClass
                                            {
                                                Blue = 0.8039216,
                                                Green = 0.88235295,
                                                Red = 0.7176471
                                            }
                                        },
                                        TextFormat = new FormatTextFormat
                                        {
                                            ForegroundColor = new BackgroundColorClass(),
                                            ForegroundColorStyle = new BackgroundColorStyle
                                            {
                                                RgbColor = new BackgroundColorClass()
                                            }
                                        }
                                    }
                                },
                                Ranges = new CreateRange[]
                                {
                                    new CreateRange
                                    {
                                        EndColumnIndex = statusRow + 1,
                                        EndRowIndex = SheetsCatalogImportConstants.DEFAULT_SHEET_SIZE,
                                        StartColumnIndex = statusRow,
                                        StartRowIndex = 1
                                    }
                                }
                            },
                        }
                    },
                    new Sheet
                    {
                        Properties = new SheetProperties
                        {
                            SheetId = 1,
                            Title = instructionsLabel,
                            Index = 1,
                            GridProperties = new GridProperties
                            {
                                ColumnCount = headerRowLabels.Count(),
                                RowCount = SheetsCatalogImportConstants.DEFAULT_SHEET_SIZE
                            },
                            SheetType = "GRID"
                        }
                    },
                    new Sheet
                    {
                        Properties = new SheetProperties
                        {
                            SheetId = 2,
                            Title = imagesLabel,
                            Index = 2,
                            GridProperties = new GridProperties
                            {
                                ColumnCount = 3,
                                RowCount = 500
                            },
                            SheetType = "GRID"
                        }
                    },
                    new Sheet
                    {
                        Properties = new SheetProperties
                        {
                            SheetId = 3,
                            Title = validationLabel,
                            Index = 3,
                            GridProperties = new GridProperties
                            {
                                ColumnCount = 2,
                                RowCount = 100000
                            },
                            SheetType = "GRID"
                        }
                    }
                }
            };

            string sheetId = await this.CreateSpreadsheet(googleSheetCreate);

            if (!string.IsNullOrEmpty(sheetId))
            {
                string lastHeaderColumnLetter = ((char)headerRowLabels.Count() + 65).ToString();

                ValueRange valueRange = new ValueRange
                {
                    MajorDimension = "ROWS",
                    Range = $"{sheetLabel}!A1:{lastHeaderColumnLetter}1",
                    Values = new string[][]
                    {
                        headerRowLabels
                    }
                };

                await this.WriteSpreadsheetValues(sheetId, valueRange);

                valueRange = new ValueRange
                {
                    MajorDimension = "ROWS",
                    Range = $"{sheetLabel}!A2:{lastHeaderColumnLetter}2",
                    Values = new string[][]
                    {
                        new string[] { "10","500", "Example/Sample", "Example", "Example Product", "X-MP","","Example Sku","8888", "SK-X-MP", "1","1","1","1","This is an example product","example,sample,demo", "This is an example product", "https://sample.demo.com/example.jpg", "","","","","TRUE","80.00","74.99","100","Material:Plastic,Wood,Glass\nBodyPart:Back,Front\nColor:Red,Yellow,Blue","Color:Blue\nMaterial:Plastic","FALSE","FALSE","","" },
                    }
                };

                await this.WriteSpreadsheetValues(sheetId, valueRange);

                valueRange = new ValueRange
                {
                    MajorDimension = "ROWS",
                    Range = $"{instructionsLabel}!A2:{lastHeaderColumnLetter}2",
                    Values = new string[][]
                    {
                        new string[] {"A few tips on using the Google Catalog Import:"},
                        new string[] {""},
                        new string[] {"The required fields are category, brand, product name, product reference code, sku name, and product description"},
                        new string[] {"For categories, you can create a tree structure such as clothing/sweaters"},
                        new string[] {"Product and SKU specs must be formatted properly. The correct formatting is\nkey:value\ngroup!key:value"},
                        new string[] {"If there are multiple specs, you can seperate them with a line break."},
                        new string[] {"Image links must be public to work properly."},
                        new string[] {"A sku must have an image to be active."},
                        new string[] {"Catalog V2: When creating a new product, do not specify a Product Id"},
                        new string[] {""},
                        new string[] {"Sample formatting can be seen below:"},
                        new string[] {""},
                        headerRowLabels,
                        new string[] { "10","500", "Example/Sample", "Example", "Example Product", "X-MP","Example Sku","8888", "SK-X-MP", "1","1","1","1","This is an example product","example,sample,demo", "This is an example product", "https://sample.demo.com/example.jpg", "","","","","TRUE","80.00","74.99","100","Material:Plastic,Wood,Glass\nBodyPart:Back,Front\nColor:Red,Yellow,Blue","Color:Blue\nMaterial:Plastic","FALSE","FALSE","","" },
                    }
                };

                await this.WriteSpreadsheetValues(sheetId, valueRange);


                BatchUpdate batchUpdate = new BatchUpdate
                {
                    Requests = new Request[]
                    {
                        new Request
                        {
                            RepeatCell = new RepeatCell
                            {
                                Cell = new Cell
                                {
                                    UserEnteredFormat = new UserEnteredFormat
                                    {
                                        HorizontalAlignment = "CENTER",
                                        BackgroundColor = new GroundColor
                                        {
                                            Blue = 0.0,
                                            Green = 0.0,
                                            Red = 0.0
                                        },
                                        TextFormat = new BatchUpdateTextFormat
                                        {
                                            Bold = true,
                                            FontSize = 12,
                                            ForegroundColor = new GroundColor
                                            {
                                                Blue = 1.0,
                                                Green = 1.0,
                                                Red = 1.0
                                            }
                                        }
                                    }
                                },
                                Fields = "userEnteredFormat(backgroundColor,textFormat,horizontalAlignment)",
                                Range = new BatchUpdateRange
                                {
                                    StartRowIndex = 0,
                                    EndRowIndex = 1,
                                    SheetId = 0
                                }
                            }
                        },
                        new Request
                        {
                            UpdateSheetProperties = new UpdateSheetProperties
                            {
                                Fields = "gridProperties.frozenRowCount",
                                Properties = new Properties
                                {
                                    SheetId = 0,
                                    GridProperties = new BatchUpdateGridProperties
                                    {
                                        FrozenRowCount = 1
                                    }
                                }
                            }
                        },
                        new Request
                        {
                            SetDataValidation = new SetDataValidation
                            {
                                Range = new BatchUpdateRange
                                {
                                    StartRowIndex = 1,
                                    EndRowIndex = SheetsCatalogImportConstants.DEFAULT_SHEET_SIZE,
                                    SheetId = 0,
                                    EndColumnIndex = displayIfOutOfStockRow + 1,
                                    StartColumnIndex = displayIfOutOfStockRow
                                },
                                Rule = new Rule
                                {
                                    Condition = new Condition
                                    {
                                        Type = "ONE_OF_LIST",
                                        Values = new Value[]
                                        {
                                            new Value
                                            {
                                                UserEnteredValue = string.Empty
                                            },
                                            new Value
                                            {
                                                UserEnteredValue = "TRUE"
                                            },
                                            new Value
                                            {
                                                UserEnteredValue = "FALSE"
                                            }
                                        }
                                    },
                                    InputMessage = $"Valid values: True / False",
                                    Strict = true
                                }
                            }
                        },
                        new Request
                        {
                            SetDataValidation = new SetDataValidation
                            {
                                Range = new BatchUpdateRange
                                {
                                    StartRowIndex = 1,
                                    EndRowIndex = SheetsCatalogImportConstants.DEFAULT_SHEET_SIZE,
                                    SheetId = 0,
                                    EndColumnIndex = updateRow + 1,
                                    StartColumnIndex = updateRow
                                },
                                Rule = new Rule
                                {
                                    Condition = new Condition
                                    {
                                        Type = "ONE_OF_LIST",
                                        Values = new Value[]
                                        {
                                            new Value
                                            {
                                                UserEnteredValue = string.Empty
                                            },
                                            new Value
                                            {
                                                UserEnteredValue = "TRUE"
                                            },
                                            new Value
                                            {
                                                UserEnteredValue = "FALSE"
                                            }
                                        }
                                    },
                                    InputMessage = $"Valid values: True / False",
                                    Strict = true
                                }
                            }
                        },
                        new Request
                        {
                            SetDataValidation = new SetDataValidation
                            {
                                Range = new BatchUpdateRange
                                {
                                    StartRowIndex = 1,
                                    EndRowIndex = SheetsCatalogImportConstants.DEFAULT_SHEET_SIZE,
                                    SheetId = 0,
                                    EndColumnIndex = activateSkuRow + 1,
                                    StartColumnIndex = activateSkuRow
                                },
                                Rule = new Rule
                                {
                                    Condition = new Condition
                                    {
                                        Type = "ONE_OF_LIST",
                                        Values = new Value[]
                                        {
                                            new Value
                                            {
                                                UserEnteredValue = string.Empty
                                            },
                                            new Value
                                            {
                                                UserEnteredValue = "TRUE"
                                            },
                                            new Value
                                            {
                                                UserEnteredValue = "FALSE"
                                            }
                                        }
                                    },
                                    InputMessage = $"Valid values: True / False",
                                    Strict = true
                                }
                            }
                        },
                        new Request
                        {
                            AutoResizeDimensions = new AutoResizeDimensions
                            {
                                Dimensions = new Dimensions
                                {
                                    Dimension = "COLUMNS",
                                    EndIndex = headerRowLabels.Count(),
                                    StartIndex = 0,
                                    SheetId = 0
                                }
                            }
                        }
                    }
                };
                
                await this.UpdateSpreadsheet(sheetId, batchUpdate);

                valueRange = new ValueRange
                {
                    MajorDimension = "ROWS",
                    Range = $"{imagesLabel}!A1:C1",
                    Values = new string[][]
                    {
                        new string[] {"Name", "Thumbnail", "Link"}
                    }
                };

                await this.WriteSpreadsheetValues(sheetId, valueRange);

                string importFolderId = null;
                string accountFolderId = null;
                string productsFolderId = null;
                string accountName = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME];

                FolderIds folderIds = await _sheetsCatalogImportRepository.LoadFolderIds(accountName);
                if (folderIds != null)
                {
                    importFolderId = folderIds.ImportFolderId;
                    accountFolderId = folderIds.AccountFolderId;
                    productsFolderId = folderIds.ProductsFolderId;
                }
                else
                {
                    ListFilesResponse getFoldersResponse = await this.GetFolders();
                    if (getFoldersResponse != null)
                    {
                        importFolderId = getFoldersResponse.Files.Where(f => f.Name.Equals(SheetsCatalogImportConstants.FolderNames.IMPORT)).Select(f => f.Id).FirstOrDefault();
                        if (!string.IsNullOrEmpty(importFolderId))
                        {
                            accountFolderId = getFoldersResponse.Files.Where(f => f.Name.Equals(accountName) && f.Parents.Contains(importFolderId)).Select(f => f.Id).FirstOrDefault();
                            if (!string.IsNullOrEmpty(accountFolderId))
                            {
                                productsFolderId = getFoldersResponse.Files.Where(f => f.Name.Equals(SheetsCatalogImportConstants.FolderNames.PRODUCTS) && f.Parents.Contains(accountFolderId)).Select(f => f.Id).FirstOrDefault();
                            }
                        }
                    }
                }

                // If any essential folders are missing verify and create the folder structure.
                if (string.IsNullOrEmpty(productsFolderId))
                {
                    folderIds = null;
                    _context.Vtex.Logger.Info("SheetImport", null, "Verifying folder structure.");
                    Dictionary<string, string> folders = await this.ListFolders();   // Id, Name

                    if (folders == null)
                    {
                        return ($"Error accessing Drive.");
                    }

                    if (!folders.ContainsValue(SheetsCatalogImportConstants.FolderNames.IMPORT))
                    {
                        importFolderId = await this.CreateFolder(SheetsCatalogImportConstants.FolderNames.IMPORT);
                    }
                    else
                    {
                        importFolderId = folders.FirstOrDefault(x => x.Value == SheetsCatalogImportConstants.FolderNames.IMPORT).Key;
                    }

                    if (string.IsNullOrEmpty(importFolderId))
                    {
                        _context.Vtex.Logger.Info("SheetImport", null, $"Could not find '{SheetsCatalogImportConstants.FolderNames.IMPORT}' folder");
                        return ($"Could not find {SheetsCatalogImportConstants.FolderNames.IMPORT} folder");
                    }

                    folders = await this.ListFolders(importFolderId);

                    if (!folders.ContainsValue(accountName))
                    {
                        accountFolderId = await this.CreateFolder(accountName, importFolderId);
                    }
                    else
                    {
                        accountFolderId = folders.FirstOrDefault(x => x.Value == accountName).Key;
                    }

                    if (string.IsNullOrEmpty(accountFolderId))
                    {
                        _context.Vtex.Logger.Info("SheetImport", null, $"Could not find {accountFolderId} folder");
                        return ($"Could not find {accountFolderId} folder");
                    }

                    folders = await this.ListFolders(accountFolderId);

                    if (!folders.ContainsValue(SheetsCatalogImportConstants.FolderNames.PRODUCTS))
                    {
                        productsFolderId = await this.CreateFolder(SheetsCatalogImportConstants.FolderNames.PRODUCTS, accountFolderId);
                    }
                    else
                    {
                        productsFolderId = folders.FirstOrDefault(x => x.Value == SheetsCatalogImportConstants.FolderNames.PRODUCTS).Key;
                    }

                    if (string.IsNullOrEmpty(productsFolderId))
                    {
                        _context.Vtex.Logger.Info("SheetImport", null, $"Could not find {productsFolderId} folder");
                        return ($"Could not find {productsFolderId} folder");
                    }
                }

                if (folderIds == null)
                {
                    folderIds = new FolderIds
                    {
                        AccountFolderId = accountFolderId,
                        ImportFolderId = importFolderId,
                        ProductsFolderId = productsFolderId
                    };

                    ListFilesResponse listFilesResponse = await this.ListSheetsInFolder(productsFolderId);
                    if (listFilesResponse != null)
                    {
                        var owners = listFilesResponse.Files.Select(o => o.Owners.Distinct()).FirstOrDefault();
                        if (owners != null)
                        {
                            folderIds.FolderOwner = owners.Select(o => o.EmailAddress).FirstOrDefault();
                        }
                    }

                    await _sheetsCatalogImportRepository.SaveFolderIds(folderIds, accountName);
                }

                await this.MoveFile(sheetId, productsFolderId);
                await SetPermission(sheetId);
            }
            else
            {
                _context.Vtex.Logger.Error("CreateSheet", null, "Failed to create sheet.");
            }

            return (await this.GetSheetLink());
        }

        public async Task<string> CreateSpreadsheet(GoogleSheetCreate googleSheetRequest)
        {
            bool success = false;
            string responseContent = string.Empty;
            string fileId = string.Empty;
            GoogleSheetCreate googleSheetResponse;

            if (googleSheetRequest != null)
            {
                Token token = await this.GetGoogleToken();
                if (token != null && !string.IsNullOrEmpty(token.AccessToken))
                {
                    var jsonSerializedMetadata = JsonConvert.SerializeObject(googleSheetRequest);

                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Post,
                        RequestUri = new Uri($"{SheetsCatalogImportConstants.GOOGLE_SHEET_URL}/{SheetsCatalogImportConstants.GOOGLE_DRIVE_SHEETS}"),
                        Content = new StringContent(jsonSerializedMetadata, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                    };

                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, $"{token.TokenType} {token.AccessToken}");

                    string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                    if (authToken != null)
                    {
                        request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    }

                    var client = _clientFactory.CreateClient();
                    try
                    {
                        var response = await client.SendAsync(request);
                        responseContent = await response.Content.ReadAsStringAsync();
                        success = response.IsSuccessStatusCode;
                        if (success)
                        {
                            googleSheetResponse = JsonConvert.DeserializeObject<GoogleSheetCreate>(responseContent);
                            fileId = googleSheetResponse.SpreadsheetId;
                        }
                    }
                    catch (Exception ex)
                    {
                        _context.Vtex.Logger.Error("CreateSpreadsheet", null, $"{jsonSerializedMetadata}", ex);
                    }
                }
                else
                {
                    _context.Vtex.Logger.Warn("CreateSpreadsheet", null, "Token error.");
                }
            }
            else
            {
                _context.Vtex.Logger.Warn("CreateSpreadsheet", null, "Parameter missing.");
            }

            return fileId;
        }

        public async Task<UpdateValuesResponse> WriteSpreadsheetValues(string fileId, ValueRange valueRange)
        {
            string responseContent = string.Empty;
            UpdateValuesResponse updateValuesResponse = null;

            if (!string.IsNullOrEmpty(fileId) && valueRange != null)
            {
                Token token = await this.GetGoogleToken();
                if (token != null && !string.IsNullOrEmpty(token.AccessToken))
                {
                    var jsonSerializedMetadata = JsonConvert.SerializeObject(valueRange);
                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Put,
                        RequestUri = new Uri($"{SheetsCatalogImportConstants.GOOGLE_SHEET_URL}/{SheetsCatalogImportConstants.GOOGLE_DRIVE_SHEETS}/{fileId}/values/{valueRange.Range}?valueInputOption=USER_ENTERED"),
                        Content = new StringContent(jsonSerializedMetadata, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                    };

                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, $"{token.TokenType} {token.AccessToken}");

                    string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                    if (authToken != null)
                    {
                        request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    }

                    var client = _clientFactory.CreateClient();
                    try
                    {
                        var response = await client.SendAsync(request);
                        responseContent = await response.Content.ReadAsStringAsync();
                        if (response.IsSuccessStatusCode)
                        {
                            updateValuesResponse = JsonConvert.DeserializeObject<UpdateValuesResponse>(responseContent);
                        }
                        else
                        {
                            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                            {
                                _context.Vtex.Logger.Warn("WriteSpreadsheetValues", null, $"Retrying [{response.StatusCode}] {responseContent} ");
                                await Task.Delay(1000 * 60);
                                client = _clientFactory.CreateClient();
                                response = await client.SendAsync(request);
                                if (response.IsSuccessStatusCode)
                                {
                                    responseContent = await response.Content.ReadAsStringAsync();
                                    updateValuesResponse = JsonConvert.DeserializeObject<UpdateValuesResponse>(responseContent);
                                }
                                else
                                {
                                    _context.Vtex.Logger.Error("WriteSpreadsheetValues", null, $"Did not update sheet [{response.StatusCode}] {responseContent} ");
                                }
                            }
                            else
                            {
                                _context.Vtex.Logger.Error("WriteSpreadsheetValues", null, $"[{response.StatusCode}] {responseContent} ");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _context.Vtex.Logger.Error("WriteSpreadsheetValues", null, $"Error writing to Sheet", ex);
                    }
                }
                else
                {
                    _context.Vtex.Logger.Warn("WriteSpreadsheetValues", null, "Token error.");
                }
            }
            else
            {
                _context.Vtex.Logger.Warn("WriteSpreadsheetValues", null, "Parameter missing.");
            }

            return updateValuesResponse;
        }

        public async Task<string> UpdateSpreadsheet(string fileId, BatchUpdate batchUpdate)
        {
            string responseContent = string.Empty;

            if (batchUpdate != null)
            {
                Token token = await this.GetGoogleToken();
                if (token != null && !string.IsNullOrEmpty(token.AccessToken))
                {
                    var jsonSerializedMetadata = JsonConvert.SerializeObject(batchUpdate);

                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Post,
                        RequestUri = new Uri($"{SheetsCatalogImportConstants.GOOGLE_SHEET_URL}/{SheetsCatalogImportConstants.GOOGLE_DRIVE_SHEETS}/{fileId}:batchUpdate"),
                        Content = new StringContent(jsonSerializedMetadata, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                    };

                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, $"{token.TokenType} {token.AccessToken}");

                    string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                    if (authToken != null)
                    {
                        request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    }

                    var client = _clientFactory.CreateClient();
                    try
                    {
                        var response = await client.SendAsync(request);
                        responseContent = await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode)
                        {
                            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                            {
                                _context.Vtex.Logger.Warn("UpdateSpreadsheet", null, $"Retrying [{response.StatusCode}] {responseContent} ");
                                await Task.Delay(1000 * 60);
                                client = _clientFactory.CreateClient();
                                response = await client.SendAsync(request);
                                if (response.IsSuccessStatusCode)
                                {
                                    responseContent = await response.Content.ReadAsStringAsync();
                                }
                                else
                                {
                                    _context.Vtex.Logger.Error("UpdateSpreadsheet", null, $"Did not update sheet [{response.StatusCode}] {responseContent} ");
                                }
                            }
                            else
                            {
                                _context.Vtex.Logger.Warn("UpdateSpreadsheet", null, $"Did not update sheet. [{response.StatusCode}] {responseContent} ");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _context.Vtex.Logger.Error("UpdateSpreadsheet", null, "Update Error", ex);
                    }
                }
                else
                {
                    _context.Vtex.Logger.Warn("UpdateSpreadsheet", null, "Token error.");
                }
            }
            else
            {
                _context.Vtex.Logger.Warn("UpdateSpreadsheet", null, "Parameter missing.");
            }

            return responseContent;
        }

        public async Task<string> ClearSpreadsheet(string fileId, SheetRange sheetRange)
        {
            string responseContent = string.Empty;

            if (sheetRange != null)
            {
                Token token = await this.GetGoogleToken();
                if (token != null && !string.IsNullOrEmpty(token.AccessToken))
                {
                    var jsonSerializedMetadata = JsonConvert.SerializeObject(sheetRange);

                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Post,
                        RequestUri = new Uri($"{SheetsCatalogImportConstants.GOOGLE_SHEET_URL}/{SheetsCatalogImportConstants.GOOGLE_DRIVE_SHEETS}/{fileId}/values:batchClear"),
                        Content = new StringContent(jsonSerializedMetadata, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                    };

                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, $"{token.TokenType} {token.AccessToken}");

                    string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                    if (authToken != null)
                    {
                        request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    }

                    var client = _clientFactory.CreateClient();
                    try
                    {
                        var response = await client.SendAsync(request);
                        responseContent = await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode)
                        {
                            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                            {
                                _context.Vtex.Logger.Warn("ClearSpreadsheet", null, $"Retrying [{response.StatusCode}] {responseContent} {jsonSerializedMetadata}");
                                await Task.Delay(1000 * 60);
                                client = _clientFactory.CreateClient();
                                response = await client.SendAsync(request);
                                if (response.IsSuccessStatusCode)
                                {
                                    responseContent = await response.Content.ReadAsStringAsync();
                                }
                                else
                                {
                                    _context.Vtex.Logger.Error("ClearSpreadsheet", null, $"Did not clear sheet [{response.StatusCode}] {responseContent} {jsonSerializedMetadata}");
                                }
                            }
                            else
                            {
                                _context.Vtex.Logger.Warn("ClearSpreadsheet", null, $"Did not clear sheet. [{response.StatusCode}] {responseContent} {jsonSerializedMetadata}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _context.Vtex.Logger.Error("ClearSpreadsheet", null, $"{jsonSerializedMetadata}", ex);
                    }
                }
                else
                {
                    _context.Vtex.Logger.Warn("ClearSpreadsheet", null, "Token error.");
                }
            }
            else
            {
                _context.Vtex.Logger.Warn("ClearSpreadsheet", null, "Parameter missing.");
            }

            return responseContent;
        }

        public async Task<string> GetSheetLink()
        {
            string sheetUrl = string.Empty;
            string importFolderId = null;
            string accountFolderId = null;
            string productsFolderId = null;
            string accountName = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME];

            FolderIds folderIds = await _sheetsCatalogImportRepository.LoadFolderIds(accountName);
            if (folderIds != null)
            {
                importFolderId = folderIds.ImportFolderId;
                accountFolderId = folderIds.AccountFolderId;
                productsFolderId = folderIds.ProductsFolderId;
            }
            else
            {
                ListFilesResponse getFoldersResponse = await this.GetFolders();
                if (getFoldersResponse != null)
                {
                    importFolderId = getFoldersResponse.Files.Where(f => f.Name.Equals(SheetsCatalogImportConstants.FolderNames.IMPORT)).Select(f => f.Id).FirstOrDefault();
                    if (!string.IsNullOrEmpty(importFolderId))
                    {
                        accountFolderId = getFoldersResponse.Files.Where(f => f.Name.Equals(accountName) && f.Parents.Contains(importFolderId)).Select(f => f.Id).FirstOrDefault();
                        if (!string.IsNullOrEmpty(accountFolderId))
                        {
                            productsFolderId = getFoldersResponse.Files.Where(f => f.Name.Equals(SheetsCatalogImportConstants.FolderNames.PRODUCTS) && f.Parents.Contains(accountFolderId)).Select(f => f.Id).FirstOrDefault();
                        }
                    }
                }
            }

            // If any essential folders are missing verify and create the folder structure.
            if (string.IsNullOrEmpty(productsFolderId))
            {
                folderIds = null;
                _context.Vtex.Logger.Info("GetSheetLink", null, "Verifying folder structure.");
                Dictionary<string, string> folders = await this.ListFolders();   // Id, Name

                if (folders == null)
                {
                    return ($"Error accessing Drive.");
                }

                if (!folders.ContainsValue(SheetsCatalogImportConstants.FolderNames.IMPORT))
                {
                    importFolderId = await this.CreateFolder(SheetsCatalogImportConstants.FolderNames.IMPORT);
                }
                else
                {
                    importFolderId = folders.FirstOrDefault(x => x.Value == SheetsCatalogImportConstants.FolderNames.IMPORT).Key;
                }

                if (string.IsNullOrEmpty(importFolderId))
                {
                    _context.Vtex.Logger.Info("GetSheetLink", null, $"Could not find '{SheetsCatalogImportConstants.FolderNames.IMPORT}' folder");
                    return ($"Could not find {SheetsCatalogImportConstants.FolderNames.IMPORT} folder");
                }

                folders = await this.ListFolders(importFolderId);

                if (!folders.ContainsValue(accountName))
                {
                    accountFolderId = await this.CreateFolder(accountName, importFolderId);
                }
                else
                {
                    accountFolderId = folders.FirstOrDefault(x => x.Value == accountName).Key;
                }

                if (string.IsNullOrEmpty(accountFolderId))
                {
                    _context.Vtex.Logger.Info("GetSheetLink", null, $"Could not find {accountFolderId} folder");
                    return ($"Could not find {accountFolderId} folder");
                }

                folders = await this.ListFolders(accountFolderId);

                if (!folders.ContainsValue(SheetsCatalogImportConstants.FolderNames.PRODUCTS))
                {
                    productsFolderId = await this.CreateFolder(SheetsCatalogImportConstants.FolderNames.PRODUCTS, accountFolderId);
                }
                else
                {
                    productsFolderId = folders.FirstOrDefault(x => x.Value == SheetsCatalogImportConstants.FolderNames.PRODUCTS).Key;
                }

                if (string.IsNullOrEmpty(productsFolderId))
                {
                    _context.Vtex.Logger.Info("GetSheetLink", null, $"Could not find {productsFolderId} folder");
                    return ($"Could not find {productsFolderId} folder");
                }
            }

            if (folderIds == null)
            {
                folderIds = new FolderIds
                {
                    AccountFolderId = accountFolderId,
                    ImportFolderId = importFolderId,
                    ProductsFolderId = productsFolderId
                };

                ListFilesResponse listFilesResponse = await this.ListSheetsInFolder(productsFolderId);
                if (listFilesResponse != null)
                {
                    var owners = listFilesResponse.Files.Select(o => o.Owners.Distinct()).FirstOrDefault();
                    if (owners != null)
                    {
                        folderIds.FolderOwner = owners.Select(o => o.EmailAddress).FirstOrDefault();
                    }
                }

                await _sheetsCatalogImportRepository.SaveFolderIds(folderIds, accountName);
            }

            ListFilesResponse spreadsheets = await this.ListSheetsInFolder(productsFolderId);
            List<string> links = new List<string>();
            if (spreadsheets != null)
            {
                foreach (GoogleFile file in spreadsheets.Files)
                {
                    links.Add(file.WebViewLink.ToString());
                }

                sheetUrl = string.Join("<br>", links);
            }

            return (sheetUrl);
        }

        public async Task<string> GetOwnerEmail()
        {
            string email = null;
            string accountName = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME];
            try
            {
                Token token = await this.GetGoogleToken();
                if (token != null)
                {
                    string productFolderId = string.Empty;
                    FolderIds folderIds = await _sheetsCatalogImportRepository.LoadFolderIds(accountName);
                    if (folderIds != null)
                    {
                        productFolderId = folderIds.ProductsFolderId;
                        _context.Vtex.Logger.Info("GetOwnerEmail", null, $"Products Folder Id: {productFolderId}");
                    }

                    ListFilesResponse listFilesResponse = await this.ListSheetsInFolder(string.Empty);
                    if (listFilesResponse != null)
                    {
                        var owners = listFilesResponse.Files.Where(f => f.Id.Equals(productFolderId)).Select(o => o.Owners.Distinct()).FirstOrDefault();
                        if (owners != null)
                        {
                            email = owners.Select(o => o.EmailAddress).FirstOrDefault();
                        }
                    }
                }
                else
                {
                    _context.Vtex.Logger.Info("GetOwnerEmail", null, "Could not load Token.");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("GetOwnerEmail", null, $"Error getting Drive owner", ex);
            }

            _context.Vtex.Logger.Info("GetOwnerEmail", null, $"Email = {email}");

            return email;
        }

        public async Task<bool> ShareToken(Token token)
        {
            bool success = false;
            string jsonSerializedMetadata = JsonConvert.SerializeObject(token);

            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.myvtex.com/google-drive-import/share-token"),
                Content = new StringContent(jsonSerializedMetadata, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_FORM)
            };

            string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
            if (authToken != null)
            {
                request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
            }

            var client = _clientFactory.CreateClient();
            try
            {
                var response = await client.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    success = true;
                }
                else
                {
                    _context.Vtex.Logger.Info("ShareToken", null, $"{response.StatusCode} {responseContent}");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("ShareToken", null, $"ShareToken Error {jsonSerializedMetadata}", ex);
            }

            return success;
        }

        public async Task<bool> BatchUpdate(string sheetId, BatchUpdate batchUpdate)
        {
            await this.UpdateSpreadsheet(sheetId, batchUpdate);
            return true;
        }
    }
}
