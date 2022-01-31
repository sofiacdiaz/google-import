﻿using SheetsCatalogImport.Data;
using Microsoft.AspNetCore.Http;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Vtex.Api.Context;
using SheetsCatalogImport.Models;
using System.Text;
using Newtonsoft.Json;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SheetsCatalogImport.Services
{
    public class VtexApiService : IVtexApiService
    {
        private readonly IIOServiceContext _context;
        private readonly IVtexEnvironmentVariableProvider _environmentVariableProvider;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IHttpClientFactory _clientFactory;
        private readonly ISheetsCatalogImportRepository _sheetsCatalogImportRepository;
        private readonly IGoogleSheetsService _googleSheetsService;
        private readonly string _applicationName;

        public VtexApiService(IIOServiceContext context, IVtexEnvironmentVariableProvider environmentVariableProvider, IHttpContextAccessor httpContextAccessor, IHttpClientFactory clientFactory, ISheetsCatalogImportRepository sheetsCatalogImportRepository, IGoogleSheetsService googleSheetsService)
        {
            this._context = context ??
                            throw new ArgumentNullException(nameof(context));

            this._environmentVariableProvider = environmentVariableProvider ??
                                                throw new ArgumentNullException(nameof(environmentVariableProvider));

            this._httpContextAccessor = httpContextAccessor ??
                                        throw new ArgumentNullException(nameof(httpContextAccessor));

            this._clientFactory = clientFactory ??
                               throw new ArgumentNullException(nameof(clientFactory));

            this._sheetsCatalogImportRepository = sheetsCatalogImportRepository ??
                               throw new ArgumentNullException(nameof(sheetsCatalogImportRepository));

            this._googleSheetsService = googleSheetsService ??
                               throw new ArgumentNullException(nameof(googleSheetsService));

            this._applicationName =
                $"{this._environmentVariableProvider.ApplicationVendor}.{this._environmentVariableProvider.ApplicationName}";
        }

        public async Task<ProcessResult> ProcessSheet()
        {
            bool isCatalogV2 = false;
            ProcessResult response = new ProcessResult();

            DateTime importStarted = await _sheetsCatalogImportRepository.CheckImportLock();
            TimeSpan elapsedTime = DateTime.Now - importStarted;
            if (elapsedTime.TotalHours < SheetsCatalogImportConstants.LOCK_TIMEOUT)
            {
                _context.Vtex.Logger.Info("ProcessSheet", null, $"Blocked by lock.  Import started: {importStarted}");
                response.Message = $"Import started {importStarted} in progress.";
                return response;
            }

            await _sheetsCatalogImportRepository.SetImportLock(DateTime.Now);
            _context.Vtex.Logger.Info("ProcessSheet", null, $"Set new lock: {DateTime.Now}");

            bool success = false;
            int doneCount = 0;
            int errorCount = 0;
            int statusColumnIndex = 0;
            StringBuilder sb = new StringBuilder();

            string importFolderId = null;
            string accountName = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME];

            FolderIds folderIds = await _sheetsCatalogImportRepository.LoadFolderIds(accountName);
            if (folderIds != null)
            {
                importFolderId = folderIds.ProductsFolderId;
            }

            ListFilesResponse spreadsheets = await _googleSheetsService.ListSheetsInFolder(importFolderId);
            if (spreadsheets != null)
            {
                AppSettings appSettings = await _sheetsCatalogImportRepository.GetAppSettings();
                if(appSettings != null)
                {
                    isCatalogV2 = appSettings.IsV2Catalog;
                }

                var sheetIds = spreadsheets.Files.Select(s => s.Id);
                foreach (var sheetId in sheetIds)
                {
                    Dictionary<string, int> headerIndexDictionary = new Dictionary<string, int>();
                    string sheetContent = await _googleSheetsService.GetSheet(sheetId, string.Empty);

                    if (string.IsNullOrEmpty(sheetContent))
                    {
                        response.Message = "Empty Spreadsheet Response.";
                        return response;
                    }

                    GoogleSheet googleSheet = JsonConvert.DeserializeObject<GoogleSheet>(sheetContent);
                    string valueRange = googleSheet.ValueRanges[0].Range;
                    string sheetName = valueRange.Split("!")[0];
                    string[] sheetHeader = googleSheet.ValueRanges[0].Values[0];
                    int headerIndex = 0;
                    int rowCount = googleSheet.ValueRanges[0].Values.Count();
                    int writeBlockSize = Math.Max(rowCount / SheetsCatalogImportConstants.WRITE_BLOCK_SIZE_DIVISOR, SheetsCatalogImportConstants.MIN_WRITE_BLOCK_SIZE);
                    string[][] arrValuesToWrite = new string[writeBlockSize][];
                    int offset = 0;
                    _context.Vtex.Logger.Debug("ProcessSheet", null, $"'{sheetName}' Row count: {rowCount} ");
                    foreach (string header in sheetHeader)
                    {
                        headerIndexDictionary.Add(header.ToLower(), headerIndex);
                        headerIndex++;
                    }

                    statusColumnIndex = headerIndexDictionary["status"];
                    string statusColumnLetter = await GetColumnLetter(headerIndexDictionary["status"]);
                    string messageColumnLetter = await GetColumnLetter(headerIndexDictionary["message"]);

                    for (int index = 1; index < rowCount; index++)
                    {
                        success = true;
                        string productid = null;
                        string skuid = null;
                        string category = null;
                        string brand = null;
                        string productName = null;
                        string productReferenceCode = null;
                        string skuName = null;
                        string skuEanGtin = null;
                        string skuReferenceCode = null;
                        string height = null;
                        string width = null;
                        string length = null;
                        string weight = null;
                        string productDescription = null;
                        string searchKeywords = null;
                        string metaTagDescription = null;
                        string imageUrl1 = null;
                        string imageUrl2 = null;
                        string imageUrl3 = null;
                        string imageUrl4 = null;
                        string imageUrl5 = null;
                        string displayIfOutOfStock = null;
                        string msrp = null;
                        string sellingPrice = null;
                        string availableQuantity = null;
                        string productSpecs = null;
                        string skuSpecs = null;
                        string tradePolicyId = null;
                        string[] dataValues = googleSheet.ValueRanges[0].Values[index];
                        if (headerIndexDictionary.ContainsKey("productid") && headerIndexDictionary["productid"] < dataValues.Count())
                            productid = dataValues[headerIndexDictionary["productid"]];
                        if (headerIndexDictionary.ContainsKey("skuid") && headerIndexDictionary["skuid"] < dataValues.Count())
                            skuid = dataValues[headerIndexDictionary["skuid"]];
                        if (headerIndexDictionary.ContainsKey("category") && headerIndexDictionary["category"] < dataValues.Count())
                            category = dataValues[headerIndexDictionary["category"]];
                        if (headerIndexDictionary.ContainsKey("brand") && headerIndexDictionary["brand"] < dataValues.Count())
                            brand = dataValues[headerIndexDictionary["brand"]];
                        if (headerIndexDictionary.ContainsKey("productname") && headerIndexDictionary["productname"] < dataValues.Count())
                            productName = dataValues[headerIndexDictionary["productname"]];
                        if (headerIndexDictionary.ContainsKey("product reference code") && headerIndexDictionary["product reference code"] < dataValues.Count())
                            productReferenceCode = dataValues[headerIndexDictionary["product reference code"]];
                        if (headerIndexDictionary.ContainsKey("skuname") && headerIndexDictionary["skuname"] < dataValues.Count())
                            skuName = dataValues[headerIndexDictionary["skuname"]];
                        if (headerIndexDictionary.ContainsKey("sku ean/gtin") && headerIndexDictionary["sku ean/gtin"] < dataValues.Count())
                            skuEanGtin = dataValues[headerIndexDictionary["sku ean/gtin"]];
                        if (headerIndexDictionary.ContainsKey("sku reference code") && headerIndexDictionary["sku reference code"] < dataValues.Count())
                            skuReferenceCode = dataValues[headerIndexDictionary["sku reference code"]];
                        if (headerIndexDictionary.ContainsKey("height") && headerIndexDictionary["height"] < dataValues.Count())
                            height = dataValues[headerIndexDictionary["height"]];
                        if (headerIndexDictionary.ContainsKey("width") && headerIndexDictionary["width"] < dataValues.Count())
                            width = dataValues[headerIndexDictionary["width"]];
                        if (headerIndexDictionary.ContainsKey("length") && headerIndexDictionary["length"] < dataValues.Count())
                            length = dataValues[headerIndexDictionary["length"]];
                        if (headerIndexDictionary.ContainsKey("weight") && headerIndexDictionary["weight"] < dataValues.Count())
                            weight = dataValues[headerIndexDictionary["weight"]];
                        if (headerIndexDictionary.ContainsKey("product description") && headerIndexDictionary["product description"] < dataValues.Count())
                            productDescription = dataValues[headerIndexDictionary["product description"]];
                        if (headerIndexDictionary.ContainsKey("search keywords") && headerIndexDictionary["search keywords"] < dataValues.Count())
                            searchKeywords = dataValues[headerIndexDictionary["search keywords"]];
                        if (headerIndexDictionary.ContainsKey("metatag description") && headerIndexDictionary["metatag description"] < dataValues.Count())
                            metaTagDescription = dataValues[headerIndexDictionary["metatag description"]];
                        if (headerIndexDictionary.ContainsKey("image url 1") && headerIndexDictionary["image url 1"] < dataValues.Count())
                            imageUrl1 = dataValues[headerIndexDictionary["image url 1"]];
                        if (headerIndexDictionary.ContainsKey("image url 2") && headerIndexDictionary["image url 2"] < dataValues.Count())
                            imageUrl2 = dataValues[headerIndexDictionary["image url 2"]];
                        if (headerIndexDictionary.ContainsKey("image url 3") && headerIndexDictionary["image url 3"] < dataValues.Count())
                            imageUrl3 = dataValues[headerIndexDictionary["image url 3"]];
                        if (headerIndexDictionary.ContainsKey("image url 4") && headerIndexDictionary["image url 4"] < dataValues.Count())
                            imageUrl4 = dataValues[headerIndexDictionary["image url 4"]];
                        if (headerIndexDictionary.ContainsKey("image url 5") && headerIndexDictionary["image url 5"] < dataValues.Count())
                            imageUrl5 = dataValues[headerIndexDictionary["image url 5"]];
                        if (headerIndexDictionary.ContainsKey("display if out of stock") && headerIndexDictionary["display if out of stock"] < dataValues.Count())
                            displayIfOutOfStock = dataValues[headerIndexDictionary["display if out of stock"]];
                        if (headerIndexDictionary.ContainsKey("msrp") && headerIndexDictionary["msrp"] < dataValues.Count())
                            msrp = dataValues[headerIndexDictionary["msrp"]];
                        if (headerIndexDictionary.ContainsKey("selling price (price to gpp)") && headerIndexDictionary["selling price (price to gpp)"] < dataValues.Count())
                            sellingPrice = dataValues[headerIndexDictionary["selling price (price to gpp)"]];
                        if (headerIndexDictionary.ContainsKey("available quantity") && headerIndexDictionary["available quantity"] < dataValues.Count())
                            availableQuantity = dataValues[headerIndexDictionary["available quantity"]];
                        if (headerIndexDictionary.ContainsKey("productspecs") && headerIndexDictionary["productspecs"] < dataValues.Count())
                            productSpecs = dataValues[headerIndexDictionary["productspecs"]];
                        if (headerIndexDictionary.ContainsKey("sku specs") && headerIndexDictionary["sku specs"] < dataValues.Count())
                            skuSpecs = dataValues[headerIndexDictionary["sku specs"]];
                        if (headerIndexDictionary.ContainsKey("trade policy id") && headerIndexDictionary["trade policy id"] < dataValues.Count())
                            tradePolicyId = dataValues[headerIndexDictionary["trade policy id"]];

                        string status = string.Empty;
                        if (headerIndexDictionary.ContainsKey("status") && headerIndexDictionary["status"] < dataValues.Count())
                            status = dataValues[headerIndexDictionary["status"]];

                        string doUpdateValue = string.Empty;
                        bool doUpdate = false;
                        if (headerIndexDictionary.ContainsKey("update") && headerIndexDictionary["update"] < dataValues.Count())
                            doUpdateValue = dataValues[headerIndexDictionary["update"]];
                        doUpdate = await ParseBool(doUpdateValue);

                        string activateSkuValue = string.Empty;
                        bool activateSku = false;
                        if (headerIndexDictionary.ContainsKey("activate sku") && headerIndexDictionary["activate sku"] < dataValues.Count())
                            activateSkuValue = dataValues[headerIndexDictionary["activate sku"]];
                        activateSku = await ParseBool(activateSkuValue);

                        if (status.Equals("Done"))
                        {
                            // skip
                            string[] arrLineValuesToWrite = new string[] { null, null };
                            arrValuesToWrite[index - offset - 1] = arrLineValuesToWrite;
                            sb.Clear();
                        }
                        else
                        {
                            UpdateResponse skuUpdateResponse = null;
                            SkuRequest skuRequest = null;
                            long productId = 0;
                            sb.AppendLine(DateTime.Now.ToString());
                            if(isCatalogV2)
                            {
                                success = true;
                                string brandId = string.Empty;
                                try
                                {
                                    GetBrandListV2Response getBrandList = await GetBrandListV2(accountName);
                                    if (getBrandList != null)
                                    {
                                        List<Datum> brandList = getBrandList.Data.Where(b => b.Name.Contains(brand, StringComparison.OrdinalIgnoreCase)).ToList();
                                        try
                                        {
                                            brandId = brandList.Select(b => b.Id).FirstOrDefault().ToString();
                                        }
                                        catch(Exception ex)
                                        {
                                            _context.Vtex.Logger.Error("ProcessSheet", "BrandV2", $"Error getting Brand Id for '{brand}' ", ex);
                                        }
                                    }

                                    if(string.IsNullOrEmpty(brandId))
                                    {
                                        CreateBrandV2Response createBrandV2Response = await this.CreateBrandV2(brand);
                                        if(createBrandV2Response != null)
                                        {
                                            brandId = createBrandV2Response.Id;
                                            _context.Vtex.Logger.Info("ProcessSheet", "BrandV2", $"Created Brand '{brand}' {brandId}");
                                            sb.AppendLine($"Created Brand '{brand}' Id: {brandId}");
                                        }
                                        else
                                        {
                                            _context.Vtex.Logger.Info("ProcessSheet", "BrandV2", $"Failed to create Brand '{brand}' {brandId}");
                                        }
                                    }
                                }
                                catch(Exception ex)
                                {
                                    _context.Vtex.Logger.Error("ProcessSheet", "BrandV2", $"Error with Brand '{brand}' {brandId}", ex);
                                }

                                string categoryId = string.Empty;
                                try
                                {
                                    GetCategoryListV2Response getCategoryList = await GetCategoryListV2(accountName);
                                    if (getCategoryList != null)
                                    {
                                        var catList = getCategoryList.Roots.Where(c => c.Value.Name.Contains(category, StringComparison.OrdinalIgnoreCase)).ToList();
                                        try
                                        {
                                            categoryId = catList.Select(c => c.Value.Id).FirstOrDefault().ToString();
                                        }
                                        catch(Exception ex)
                                        {
                                            _context.Vtex.Logger.Error("ProcessSheet", "CategoryV2", $"Error getting Category Id for '{category}' ", ex);
                                        }
                                    }

                                    if (string.IsNullOrEmpty(categoryId))
                                    {
                                        CreateCategoryV2Response createCategoryV2Response = await this.CreateCategoryV2(category);
                                        if (createCategoryV2Response != null && createCategoryV2Response.Value != null)
                                        {
                                            categoryId = createCategoryV2Response.Value.Id;
                                            _context.Vtex.Logger.Info("ProcessSheet", "CategoryV2", $"Created Category '{category}' {categoryId}");
                                            sb.AppendLine($"Created Category '{category}' Id: {categoryId}");
                                        }
                                        else
                                        {
                                            _context.Vtex.Logger.Info("ProcessSheet", "CategoryV2", $"Failed to create Category '{category}' {categoryId}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _context.Vtex.Logger.Error("ProcessSheet", "CategoryV2", $"Error with Category '{category}' {categoryId}", ex);
                                }

                                ProductRequestV2 productRequest = new ProductRequestV2
                                {
                                    Id = string.IsNullOrWhiteSpace(productid) ? null : productid,
                                    Name = productName,
                                    CategoryPath = category,
                                    BrandName = brand,
                                    ExternalId = string.IsNullOrWhiteSpace(productReferenceCode) ? null : productReferenceCode,
                                    Description = productDescription,
                                    Images = new List<ProductV2Image>(),
                                    Status = "active",
                                    Condition = "new",
                                    Type = "physical",
                                    BrandId = brandId,
                                    CategoryIds = new string[]{ categoryId },
                                    Skus = new List<Skus>()
                                };

                                // Add the sku for the current line, then check the next line and add sku if the the product is the same
                                SkusSpec[] skusSpecs = null;
                                if (!string.IsNullOrEmpty(skuSpecs))
                                {
                                    try
                                    {
                                        string[] allSkuSpecs = skuSpecs.Split(new string[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                                        skusSpecs = new SkusSpec[allSkuSpecs.Length];
                                        for (int i = 0; i < allSkuSpecs.Length; i++)
                                        {
                                            string[] skuSpecsArr = allSkuSpecs[i].Split(':');
                                            string skuSpecName = skuSpecsArr[0];
                                            if (skuSpecName.First().Equals('.'))
                                            {
                                                skuSpecName = skuSpecName.Substring(1);
                                            }

                                            if (skuSpecName.Contains("!"))
                                            {
                                                string[] skuSpecGroup = skuSpecName.Split('!');
                                                skuSpecName = skuSpecGroup[1];
                                            }

                                            string skuSpecValue = skuSpecsArr[1];

                                            SkusSpec skuSpec = new SkusSpec
                                            {
                                                Name = skuSpecName,
                                                Value = skuSpecValue
                                            };

                                            skusSpecs[i] = skuSpec;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        success = false;
                                        sb.AppendLine($"Error processing Sku Specifications.");
                                        _context.Vtex.Logger.Error("ProcessSheet", null, "Error processing Sku Spec", ex);
                                    }
                                }

                                if (string.IsNullOrWhiteSpace(skuName) && string.IsNullOrWhiteSpace(skuid))
                                {
                                    sb.AppendLine("Missing sku info");
                                }
                                else
                                {
                                    if(string.IsNullOrWhiteSpace(skuReferenceCode))
                                    {
                                        skuReferenceCode = null;
                                    }

                                    Skus sku = new Skus
                                    {
                                        Id = string.IsNullOrWhiteSpace(skuid) ? null : skuid,
                                        IsActive = activateSku,
                                        ExternalId = skuReferenceCode,
                                        Dimensions = new ProductV2Dimensions
                                        {
                                            Height = await ParseDouble(height),
                                            Length = await ParseDouble(length),
                                            Width = await ParseDouble(width),
                                        },
                                        Weight = await ParseDouble(weight),
                                        Sellers = new Seller[] { },
                                        Name = skuName,
                                        Ean = skuEanGtin,
                                        Specs = skusSpecs
                                    };

                                    productRequest.Skus.Add(sku);
                                }

                                if (!string.IsNullOrWhiteSpace(searchKeywords) && !string.IsNullOrWhiteSpace(metaTagDescription))
                                {
                                    productRequest.Attributes = new AttributeV2[]
                                    {
                                        new AttributeV2
                                        {
                                            Name = "Search Keywords",
                                            Value = searchKeywords,
                                            Description = string.Empty,
                                            IsFilterable = false
                                        },
                                        new AttributeV2
                                        {
                                            Name = "Metatag Description",
                                            Value = metaTagDescription,
                                            Description = string.Empty,
                                            IsFilterable = false
                                        }
                                    };
                                }
                                else if(!string.IsNullOrWhiteSpace(searchKeywords))
                                {
                                    productRequest.Attributes = new AttributeV2[]
                                    {
                                        new AttributeV2
                                        {
                                            Name = "Search Keywords",
                                            Value = searchKeywords,
                                            Description = string.Empty,
                                            IsFilterable = false
                                        }
                                    };
                                }
                                else if (!string.IsNullOrWhiteSpace(metaTagDescription))
                                {
                                    productRequest.Attributes = new AttributeV2[]
                                    {
                                        new AttributeV2
                                        {
                                            Name = "Metatag Description",
                                            Value = metaTagDescription,
                                            Description = string.Empty,
                                            IsFilterable = false
                                        }
                                    };
                                }

                                if (!string.IsNullOrWhiteSpace(productSpecs))
                                {
                                    try
                                    {
                                        string[] allProdSpecs = productSpecs.Split(new string[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                                        productRequest.Specs = new ProductV2Spec[allProdSpecs.Length];
                                        for (int i = 0; i < allProdSpecs.Length; i++)
                                        {
                                            string[] prodSpecsArr = allProdSpecs[i].Split(':');
                                            string prodSpecName = prodSpecsArr[0];
                                            if (prodSpecName.First().Equals('.'))
                                            {
                                                prodSpecName = prodSpecName.Substring(1);
                                            }

                                            if (prodSpecName.Contains("!"))
                                            {
                                                string[] prodSpecGroup = prodSpecName.Split('!');
                                                prodSpecName = prodSpecGroup[1];
                                            }

                                            string[] specValueArr = prodSpecsArr[1].Split(',');

                                            ProductV2Spec prodSpec = new ProductV2Spec
                                            {
                                                Name = prodSpecName,
                                                Values = specValueArr
                                            };

                                            productRequest.Specs[i] = prodSpec;
                                        }
                                    }
                                    catch(Exception ex)
                                    {
                                        success = false;
                                        sb.AppendLine($"Error processing Product Specifications.");
                                        _context.Vtex.Logger.Error("ProcessSheet", null, "Error processing Prod Spec", ex);
                                    }
                                }

                                try
                                {
                                    List<ProductV2Image> imagesList = new List<ProductV2Image>();
                                    if(!string.IsNullOrEmpty(imageUrl1))
                                    {
                                        imagesList.Add(new ProductV2Image{Url = imageUrl1, Alt = "Main", Id = "Main" });
                                    }

                                    if(!string.IsNullOrEmpty(imageUrl2))
                                    {
                                        imagesList.Add(new ProductV2Image{Url = imageUrl2, Alt = "Alt 1", Id = "Alt 1" });
                                    }

                                    if(!string.IsNullOrEmpty(imageUrl3))
                                    {
                                        imagesList.Add(new ProductV2Image{Url = imageUrl3, Alt = "Alt 2", Id = "Alt 2" });
                                    }

                                    if(!string.IsNullOrEmpty(imageUrl4))
                                    {
                                        imagesList.Add(new ProductV2Image{Url = imageUrl4, Alt = "Alt 3", Id = "Alt 3" });
                                    }

                                    if(!string.IsNullOrEmpty(imageUrl5))
                                    {
                                        imagesList.Add(new ProductV2Image{Url = imageUrl5, Alt = "Alt 4", Id = "Alt 4" });
                                    }

                                    if(imagesList.Count > 0)
                                    {
                                        productRequest.Images = imagesList;
                                        productRequest.Skus.First().Images = imagesList.Select(i => i.Id).ToArray();
                                    }
                                }
                                catch(Exception ex)
                                {
                                    success = false;
                                    sb.AppendLine($"ImagesV2: {ex.Message}");
                                }

                                try
                                {
                                    UpdateResponse productV2Response = null;
                                    ProductRequestV2 existingProduct = null;
                                    if (!string.IsNullOrEmpty(productRequest.Id))
                                    {
                                        existingProduct = await this.GetProductV2(productRequest.Id);
                                    }

                                    if (existingProduct != null)
                                    {
                                        sb.AppendLine($"Existing Product '{productRequest.Id}'");
                                        productRequest = await this.MergeProductRequestV2(existingProduct, productRequest);
                                        productV2Response = await this.UpdateProductV2(productRequest);
                                    }
                                    else if (string.IsNullOrEmpty(productRequest.Id))
                                    {
                                        productV2Response = await this.CreateProductV2(productRequest);
                                        if (productV2Response != null)
                                        {
                                            if (!productV2Response.Success && productV2Response.StatusCode.Equals("Conflict") && doUpdate)
                                            {
                                                sb.AppendLine($"[{productV2Response.StatusCode}] {productV2Response.Message}");
                                                if (productV2Response.Message.Contains("A product with external id "))
                                                {
                                                    string[] splitResponse = productV2Response.Message.Split("\"");
                                                    string externalId = splitResponse[1];
                                                    existingProduct = await GetProductByExternalIdV2(externalId);
                                                }

                                                if (existingProduct != null && !string.IsNullOrEmpty(productRequest.Id))
                                                {
                                                    existingProduct = await this.GetProductV2(productRequest.Id);
                                                }

                                                if (existingProduct != null)
                                                {
                                                    productRequest.Id = existingProduct.Id;
                                                    sb.AppendLine($"Updating Existing Product Id '{productRequest.Id}'");
                                                    productRequest = await this.MergeProductRequestV2(existingProduct, productRequest);
                                                    productV2Response = await this.UpdateProductV2(productRequest);
                                                }
                                                else
                                                {
                                                    sb.AppendLine($"Updating Product Id '{productRequest.Id}'");
                                                    productV2Response = await this.UpdateProductV2(productRequest);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            _context.Vtex.Logger.Warn("CreateProductV2", null, "CreateProductV2 NULL Response!");
                                        }
                                    }
                                    else
                                    {
                                        sb.AppendLine($"Product with Id '{productRequest.Id}' does not exist.");
                                        sb.AppendLine("Clear 'ProductId' cell to create new product.");
                                    }

                                    success &= productV2Response.Success;

                                    Console.WriteLine($"ProductV2: [{productV2Response.StatusCode}] {productV2Response.Message}");
                                    sb.AppendLine($"ProductV2: [{productV2Response.StatusCode}] {productV2Response.Message}");
                                }
                                catch (Exception ex)
                                {
                                    success = false;
                                    Console.WriteLine($"ProductV2: (err): {ex.Message}");
                                    sb.AppendLine($"ProductV2 (err): {ex.Message}");
                                }
                            }
                            else    // Catalog V1
                            {
                                ProductRequest productRequest = new ProductRequest
                                {
                                    Id = await ParseLong(productid),
                                    Name = productName,
                                    CategoryPath = category,
                                    BrandName = brand,
                                    RefId = productReferenceCode,
                                    Title = productName,
                                    LinkId = $"{productName}-{productReferenceCode}",
                                    Description = productDescription,
                                    ReleaseDate = DateTime.Now.ToString(),
                                    KeyWords = searchKeywords,
                                    IsVisible = true,
                                    IsActive = true,
                                    TaxCode = string.Empty,
                                    MetaTagDescription = metaTagDescription,
                                    ShowWithoutStock = await ParseBool(displayIfOutOfStock),
                                    Score = 1
                                };

                                UpdateResponse productUpdateResponse = await this.CreateProduct(productRequest);
                                sb.AppendLine($"Product: [{productUpdateResponse.StatusCode}] {productUpdateResponse.Message}");
                                if (productUpdateResponse.Success)
                                {
                                    ProductResponse productResponse = JsonConvert.DeserializeObject<ProductResponse>(productUpdateResponse.Message);
                                    productId = productResponse.Id;
                                    sb.AppendLine($"New Product Id {productId}");
                                    success = true;
                                }
                                else if (productUpdateResponse.StatusCode.Equals("Conflict"))
                                {
                                    // 409 - Same ID "Product already created with this Id"
                                    // 409 - Same RefId "There is already a product created with the same RefId with Product Id 100081202"
                                    // 409 - Same link Id "There is already a product with the same LinkId with Product Id 100081169"
                                    if (productUpdateResponse.Message.Contains("Product already created with this Id"))
                                    {
                                        productId = productRequest.Id ?? 0;
                                        success = true;

                                        if(doUpdate)
                                        {
                                            productUpdateResponse = await this.UpdateProduct(productid, productRequest);
                                            success = productUpdateResponse.Success;
                                            sb.AppendLine($"Product Update: [{productUpdateResponse.StatusCode}] {productUpdateResponse.Message}");
                                        }
                                    }
                                    else if (productUpdateResponse.Message.Contains("There is already a product"))
                                    {
                                        if (string.IsNullOrEmpty(productid))
                                        {
                                            string[] splitResponse = productUpdateResponse.Message.Split(" ");
                                            string parsedProductId = splitResponse[splitResponse.Length - 1];
                                            parsedProductId = parsedProductId.Remove(parsedProductId.Length - 1, 1);
                                            productId = await ParseLong(parsedProductId) ?? 0;
                                            if (productId > 0)
                                            {
                                                productid = productId.ToString();
                                                success = true;
                                                sb.AppendLine($"Using Product Id {productId}");

                                                if (doUpdate)
                                                {
                                                    productUpdateResponse = await this.UpdateProduct(productid, productRequest);
                                                    success = productUpdateResponse.Success;
                                                    sb.AppendLine($"Product Update: [{productUpdateResponse.StatusCode}] {productUpdateResponse.Message}");
                                                }
                                            }
                                            else
                                            {
                                                success = false;
                                            }
                                        }
                                        else
                                        {
                                            success = false;
                                        }
                                    }
                                    else
                                    {
                                        // What to do in this case?
                                        success = false;
                                    }
                                }
                                else
                                {
                                    // What to do in this case?
                                    success = false;
                                }
                            
                                if (success)
                                {
                                    double? packagedHeight = await ParseDouble(height);
                                    double? packagedLength = await ParseDouble(length);
                                    double? packagedWidth = await ParseDouble(width);

                                    skuRequest = new SkuRequest
                                    {
                                        Id = await ParseLong(skuid),
                                        ProductId = productId,
                                        IsActive = false,
                                        Name = skuName,
                                        RefId = skuReferenceCode,
                                        PackagedHeight = await ParseDouble(height),
                                        PackagedLength = await ParseDouble(length),
                                        PackagedWidth = await ParseDouble(width),
                                        PackagedWeightKg = await ParseDouble(weight),
                                        CubicWeight = (packagedHeight * packagedLength * packagedWidth) / SheetsCatalogImportConstants.VOLUMETIC_FACTOR, // https://www.efulfillmentservice.com/2012/11/how-to-calculate-dimensional-weight/
                                        IsKit = false,
                                        CommercialConditionId = 1,
                                        MeasurementUnit = "un",
                                        UnitMultiplier = 1,
                                        KitItensSellApart = false
                                    };


                                    skuUpdateResponse = await this.CreateSku(skuRequest);
                                    sb.AppendLine($"Sku: [{skuUpdateResponse.StatusCode}] {skuUpdateResponse.Message}");
                                    if(skuUpdateResponse.Success && string.IsNullOrEmpty(skuid))
                                    {
                                        SkuResponse skuResponse = JsonConvert.DeserializeObject<SkuResponse>(skuUpdateResponse.Message);
                                        skuid = skuResponse.Id.ToString();
                                        sb.AppendLine($"New Sku Id {skuid}");
                                    }

                                    if (skuUpdateResponse.StatusCode.Equals("Conflict"))
                                    {
                                        if (skuUpdateResponse.Message.Contains("Sku can not be created because the RefId is registered in Sku id"))
                                        {
                                            if (string.IsNullOrEmpty(skuid))
                                            {
                                                string[] splitResponse = skuUpdateResponse.Message.Split(" ");
                                                skuid = splitResponse[splitResponse.Length - 1];
                                                skuid = skuid.Remove(skuid.Length - 1, 1);
                                                if (string.IsNullOrEmpty(skuid))
                                                {
                                                    success &= false;
                                                }
                                                else
                                                {
                                                    success &= true;
                                                    sb.AppendLine($"Using Sku Id {skuid}");
                                                    if(doUpdate)
                                                    {
                                                        skuUpdateResponse = await this.UpdateSku(skuid, skuRequest);
                                                        success &= skuUpdateResponse.Success;
                                                        sb.AppendLine($"Sku Update: [{skuUpdateResponse.StatusCode}] {skuUpdateResponse.Message}");
                                                    }
                                                }
                                            }
                                        }
                                        else if (skuUpdateResponse.Message.Contains("Sku already created with this Id") && doUpdate)
                                        {
                                                skuUpdateResponse = await this.UpdateSku(skuid, skuRequest);
                                                success &= skuUpdateResponse.Success;
                                                sb.AppendLine($"Sku Update: [{skuUpdateResponse.StatusCode}] {skuUpdateResponse.Message}");
                                        }
                                    }
                                    else
                                    {
                                        success &= skuUpdateResponse.Success;
                                    }
                                }
                            }
                            
                            if (success && !isCatalogV2)
                            {
                                try
                                {
                                    if (!string.IsNullOrEmpty(skuEanGtin))
                                    {
                                        UpdateResponse eanResponse = await this.CreateEANGTIN(skuid, skuEanGtin);
                                        sb.AppendLine($"EAN/GTIN: [{eanResponse.StatusCode}] {eanResponse.Message}");
                                        success &= eanResponse.Success;
                                    }
                                    else
                                    {
                                        sb.AppendLine($"EAN/GTIN: Empty");
                                    }
                                }
                                catch(Exception ex)
                                {
                                    Console.WriteLine($"EAN/GTIN: {ex.Message}");
                                }
                            }
                            
                            if (success && !isCatalogV2)
                            {
                                try
                                {
                                    UpdateResponse updateResponse = null;
                                    bool imageSuccess = true;
                                    bool haveImage = false;
                                    StringBuilder imageResults = new StringBuilder();
                                    if (!string.IsNullOrEmpty(imageUrl1))
                                    {
                                        haveImage = true;
                                        updateResponse = await this.CreateSkuFile(skuid, $"{skuName}-1", $"{skuName}-1", true, imageUrl1);
                                        imageSuccess &= updateResponse.Success;
                                        imageResults.AppendLine($"1: {updateResponse.Message}");
                                    }

                                    if (!string.IsNullOrEmpty(imageUrl2))
                                    {
                                        haveImage = true;
                                        updateResponse = await this.CreateSkuFile(skuid, $"{skuName}-2", $"{skuName}-2", false, imageUrl2);
                                        imageSuccess &= updateResponse.Success;
                                        imageResults.AppendLine($"2: {updateResponse.Message}");
                                    }

                                    if (!string.IsNullOrEmpty(imageUrl3))
                                    {
                                        haveImage = true;
                                        updateResponse = await this.CreateSkuFile(skuid, $"{skuName}-3", $"{skuName}-3", false, imageUrl3);
                                        imageSuccess &= updateResponse.Success;
                                        imageResults.AppendLine($"3: {updateResponse.Message}");
                                    }

                                    if (!string.IsNullOrEmpty(imageUrl4))
                                    {
                                        haveImage = true;
                                        updateResponse = await this.CreateSkuFile(skuid, $"{skuName}-4", $"{skuName}-4", false, imageUrl4);
                                        imageSuccess &= updateResponse.Success;
                                        imageResults.AppendLine($"4: {updateResponse.Message}");
                                    }

                                    if (!string.IsNullOrEmpty(imageUrl5))
                                    {
                                        haveImage = true;
                                        updateResponse = await this.CreateSkuFile(skuid, $"{skuName}-5", $"{skuName}-5", false, imageUrl5);
                                        imageSuccess &= updateResponse.Success;
                                        imageResults.AppendLine($"5: {updateResponse.Message}");
                                    }

                                    if (haveImage)
                                    {
                                        success &= imageSuccess;
                                        sb.AppendLine($"Images: {imageSuccess} {imageResults}");
                                    }
                                    else
                                    {
                                        sb.AppendLine($"Images: Empty");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Images: {ex.Message}");
                                }
                            }
                            
                            if (success)
                            {
                                try
                                {
                                    if (!string.IsNullOrEmpty(msrp) && !string.IsNullOrEmpty(sellingPrice))
                                    {
                                        CreatePrice createPrice = new CreatePrice
                                        {
                                            BasePrice = await ParseCurrency(sellingPrice) ?? 0,
                                            ListPrice = await ParseCurrency(msrp) ?? 0,
                                            CostPrice = await ParseCurrency(msrp) ?? 0
                                        };

                                        UpdateResponse priceResponse = await this.CreatePrice(skuid, createPrice);
                                        success &= priceResponse.Success;
                                        sb.AppendLine($"Price: [{priceResponse.StatusCode}] {priceResponse.Message}");
                                    }
                                    else
                                    {
                                        sb.AppendLine($"Price: Empty");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Price: {ex.Message}");
                                }
                            }
                            
                            if (success)
                            {
                                try
                                {
                                    if (!string.IsNullOrEmpty(availableQuantity))
                                    {
                                        GetWarehousesResponse[] getWarehousesResponse = await GetWarehouses();
                                        if (getWarehousesResponse != null)
                                        {
                                            string warehouseId = getWarehousesResponse.Select(w => w.Id).FirstOrDefault();
                                            if (!string.IsNullOrEmpty(warehouseId))
                                            {
                                                InventoryRequest inventoryRequest = new InventoryRequest
                                                {
                                                    DateUtcOnBalanceSystem = null,
                                                    Quantity = await ParseLong(availableQuantity) ?? 0,
                                                    UnlimitedQuantity = false
                                                };

                                                UpdateResponse inventoryResponse = await this.SetInventory(skuid, warehouseId, inventoryRequest);
                                                success &= inventoryResponse.Success;
                                                sb.AppendLine($"Inventory: [{inventoryResponse.StatusCode}] {inventoryResponse.Message}");
                                            }
                                            else
                                            {
                                                sb.AppendLine($"Inventory: No Warehouse");
                                                success = false;
                                            }
                                        }
                                        else
                                        {
                                            sb.AppendLine($"Inventory: Null Warehouse");
                                            success = false;
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Inventory: {ex.Message}");
                                }
                            }
                            
                            if (success && !isCatalogV2)
                            {
                                try
                                {
                                    if (!string.IsNullOrEmpty(productSpecs))
                                    {
                                        try
                                        {
                                            string[] allProdSpecs = productSpecs.Split(new string[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                                            for (int i = 0; i < allProdSpecs.Length; i++)
                                            {
                                                string prodGroupName = "Default";
                                                bool rootLevelSpecification = false;
                                                string[] prodSpecsArr = allProdSpecs[i].Split(':');
                                                string prodSpecName = prodSpecsArr[0];
                                                if(prodSpecName.First().Equals('.'))
                                                {
                                                    rootLevelSpecification = true;
                                                    prodSpecName = prodSpecName.Substring(1);
                                                }

                                                if(prodSpecName.Contains("!"))
                                                {
                                                    string[] prodSpecGroup = prodSpecName.Split('!');
                                                    prodGroupName = prodSpecGroup[0];
                                                    prodSpecName = prodSpecGroup[1];
                                                }
                                                
                                                string[] prodSpecValueArr = prodSpecsArr[1].Split(',');

                                                SpecAttr prodSpec = new SpecAttr
                                                {
                                                    GroupName = prodGroupName,
                                                    RootLevelSpecification = rootLevelSpecification,
                                                    FieldName = prodSpecName,
                                                    FieldValues = prodSpecValueArr
                                                };

                                                UpdateResponse prodSpecResponse = await this.SetProdSpecs(productId.ToString(), prodSpec);
                                                if (!prodSpecResponse.Success && prodSpecResponse.StatusCode.Equals("TooManyRequests"))
                                                {
                                                    _context.Vtex.Logger.Warn("ProcessSheet", null, $"Prod Spec {i + 1}: [{prodSpecResponse.StatusCode}] - Retrying...");
                                                    await Task.Delay(1000 * 10);
                                                    prodSpecResponse = await this.SetProdSpecs(productId.ToString(), prodSpec);
                                                }

                                                success &= prodSpecResponse.Success;
                                                sb.AppendLine($"Prod Spec {i + 1}: [{prodSpecResponse.StatusCode}] {prodSpecResponse.Message}");
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            success = false;
                                            sb.AppendLine($"Error processing Product Specifications.");
                                            _context.Vtex.Logger.Error("ProcessSheet", null, "Error processing Prod Spec", ex);
                                        }
                                    }

                                    if (!string.IsNullOrEmpty(skuSpecs))
                                    {
                                        try
                                        {
                                            string[] allSpecs = skuSpecs.Split(new string[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                                            for (int i = 0; i < allSpecs.Length; i++)
                                            {
                                                string groupName = "Default";
                                                bool rootLevelSpecification = false;
                                                string[] specsArr = allSpecs[i].Split(':');
                                                string specName = specsArr[0];
                                                if (specName.First().Equals('.'))
                                                {
                                                    rootLevelSpecification = true;
                                                    specName = specName.Substring(1);
                                                }

                                                if (specName.Contains("!"))
                                                {
                                                    string[] specGroup = specName.Split('!');
                                                    groupName = specGroup[0];
                                                    specName = specGroup[1];
                                                }

                                                string[] specValueArr = specsArr[1].Split(',');

                                                SpecAttr skuSpec = new SpecAttr
                                                {
                                                    GroupName = groupName,
                                                    RootLevelSpecification = rootLevelSpecification,
                                                    FieldName = specName,
                                                    FieldValues = specValueArr
                                                };

                                                UpdateResponse skuSpecResponse = await this.SetSkuSpec(skuid, skuSpec);

                                                if (!skuSpecResponse.Success && skuSpecResponse.StatusCode.Equals("TooManyRequests"))
                                                {
                                                    _context.Vtex.Logger.Warn("ProcessSheet", null, $"Sku Spec {i + 1}: [{skuSpecResponse.StatusCode}] - Retrying...");
                                                    await Task.Delay(5000);
                                                    skuSpecResponse = await this.SetSkuSpec(skuid, skuSpec);
                                                }

                                                success &= skuSpecResponse.Success;
                                                sb.AppendLine($"Sku Spec {i + 1}: [{skuSpecResponse.StatusCode}] {skuSpecResponse.Message}");
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            success = false;
                                            sb.AppendLine($"Error processing Sku Specifications.");
                                            _context.Vtex.Logger.Error("ProcessSheet", null, "Error processing Sku Spec", ex);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Specs: {ex.Message}");
                                }
                            }
                            
                            if(success && activateSku && !isCatalogV2)
                            {
                                try
                                {
                                    skuRequest.IsActive = true;
                                    skuUpdateResponse = await this.UpdateSku(skuid, skuRequest);
                                    success &= skuUpdateResponse.Success;
                                    sb.AppendLine($"Activate Sku: [{skuUpdateResponse.StatusCode}] {skuUpdateResponse.Message}");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Activate Sku: {ex.Message}");
                                    sb.AppendLine($"Activate Sku error: {ex.Message}");
                                }
                            }

                            if (success && !string.IsNullOrEmpty(tradePolicyId))
                            {
                                try
                                {
                                    string[] tradePolicyIds = tradePolicyId.Split(',');
                                    foreach (string policyId in tradePolicyIds)
                                    {
                                        UpdateResponse tradePolicyResponse = await this.CreateProductToTradePolicy(productid, policyId);
                                        success &= tradePolicyResponse.Success;
                                        sb.AppendLine($"Trade Policy Id '{tradePolicyId}': [{skuUpdateResponse.StatusCode}] {skuUpdateResponse.Message}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Trade Policy: {ex.Message}");
                                    sb.AppendLine($"Trade Policy Id '{tradePolicyId}' error: {ex.Message}");
                                }
                            }
                            
                            string result = success ? "Done" : "Error";
                            string[] arrLineValuesToWrite = new string[] { result, sb.ToString() };
                            arrValuesToWrite[index - offset - 1] = arrLineValuesToWrite;
                            if (success)
                            {
                                doneCount++;
                            }
                            else
                            {
                                errorCount++;
                                _context.Vtex.Logger.Warn("ProcessSheet", null, $"Line {index}\n{sb}");
                            }
                            
                            sb.Clear();
                        }

                        if (index % writeBlockSize == 0 || index + 1 == rowCount)
                        {
                            ValueRange valueRangeToWrite = new ValueRange
                            {
                                Range = $"{sheetName}!{statusColumnLetter}{offset + 2}:{messageColumnLetter}{offset + writeBlockSize + 1}",
                                Values = arrValuesToWrite
                            };

                            var writeToSheetResult = await _googleSheetsService.WriteSpreadsheetValues(sheetId, valueRangeToWrite);
                            _context.Vtex.Logger.Debug("ProcessSheet", null, $"Writing to sheet {JsonConvert.SerializeObject(writeToSheetResult)}");
                            offset += writeBlockSize;
                            arrValuesToWrite = new string[writeBlockSize][];
                        }
                    }

                    BatchUpdate batchUpdate = new BatchUpdate
                    {
                        Requests = new Request[]
                        {
                            new Request
                            {
                                AutoResizeDimensions = new AutoResizeDimensions
                                {
                                    Dimensions = new Dimensions
                                    {
                                        Dimension = "COLUMNS",
                                        EndIndex = statusColumnIndex+1,
                                        StartIndex = statusColumnIndex-1,
                                        SheetId = 0
                                    }
                                }
                            }
                        }
                    };

                    await _googleSheetsService.UpdateSpreadsheet(sheetId, batchUpdate);
                }
            }

            await _sheetsCatalogImportRepository.ClearImportLock();
            response.Done = doneCount;
            response.Error = errorCount;
            _context.Vtex.Logger.Info("ProcessSheet", null, $"Done: {doneCount} Error: {errorCount}");

            return response;
        }

        public async Task<SearchTotals> SearchTotal(string query)
        {
            SearchTotals searchTotals = new SearchTotals();
            if (string.IsNullOrEmpty(query))
            {
                searchTotals.Message = "Empty Search";
            }
            else
            {
                string[] queryArr = query.Split(':');
                string queryType = queryArr[0];
                string queryParam = queryArr[1];
                if (string.IsNullOrEmpty(queryType) || string.IsNullOrEmpty(queryParam))
                {
                    searchTotals.Message = "Invalid Search";
                }
                else
                {
                    if (queryType.ToLower().Equals("category"))
                    {
                        GetCategoryTreeResponse[] categoryTree = await this.GetCategoryTree(10, string.Empty);
                        Dictionary<long, string> categoryIds = await GetCategoryId(categoryTree);
                        categoryIds = categoryIds.Where(c => c.Value.Contains(queryParam, StringComparison.OrdinalIgnoreCase)).ToDictionary(c => c.Key, c => c.Value);
                        foreach (long categoryId in categoryIds.Keys)
                        {
                            ProductAndSkuIdsResponse productAndSkuIdsResponse = await GetProductAndSkuIds(categoryId);
                            if (productAndSkuIdsResponse.Range.Total > 0)
                            {
                                foreach (KeyValuePair<string,long[]> productSku in productAndSkuIdsResponse.Data)
                                {
                                    searchTotals.Products++;
                                    searchTotals.Skus += productSku.Value.Count();
                                }
                            }
                        }
                    }
                    else if (queryType.ToLower().Equals("brand"))
                    {
                        GetBrandListResponse[] brandList = await GetBrandList(string.Empty);
                    }
                    else if (queryType.ToLower().Equals("productid"))
                    {
                        searchTotals.Products++;
                        List<ProductSkusResponse> productSkusResponse = await this.GetSkusFromProductId(queryParam);
                        searchTotals.Skus += productSkusResponse.Count;
                    }
                    else if (queryType.ToLower().Equals("product"))
                    {
                        ProductSearchResponse[] productSearchResponses = await this.ProductSearch(queryParam);
                        if(productSearchResponses != null)
                        {
                            foreach(ProductSearchResponse productSearchResponse in productSearchResponses)
                            {
                                searchTotals.Products++;
                                List<ProductSkusResponse> productSkusResponse = await this.GetSkusFromProductId(productSearchResponse.ProductId);
                                searchTotals.Skus += productSkusResponse.Count;
                            }
                        }
                    }
                }
            }

            searchTotals.TotalRecords = searchTotals.Skus;

            return searchTotals;
        }

        public async Task<string> ExportToSheet(string query)
        {
            int writeBlockSize = 5;
            string[][] arrayToWrite = new string[writeBlockSize+1][];
            string importFolderId = null;
            string accountName = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME];

            FolderIds folderIds = await _sheetsCatalogImportRepository.LoadFolderIds(accountName);
            if (folderIds != null)
            {
                importFolderId = folderIds.ProductsFolderId;
            }

            ListFilesResponse spreadsheets = await _googleSheetsService.ListSheetsInFolder(importFolderId);
            if (spreadsheets != null)
            {
                var sheetIds = spreadsheets.Files.Select(s => s.Id);
                foreach (var sheetId in sheetIds)
                {
                    Dictionary<string, int> headerIndexDictionary = new Dictionary<string, int>();
                    string sheetContent = await _googleSheetsService.GetSheet(sheetId, string.Empty);

                    if (string.IsNullOrEmpty(sheetContent))
                    {
                        return ("Empty Spreadsheet Response.");
                    }

                    GoogleSheet googleSheet = JsonConvert.DeserializeObject<GoogleSheet>(sheetContent);
                    string valueRange = googleSheet.ValueRanges[0].Range;
                    string sheetName = valueRange.Split("!")[0];
                    string[] sheetHeader = googleSheet.ValueRanges[0].Values[0];
                    int headerIndex = 0;
                    foreach (string header in sheetHeader)
                    {
                        headerIndexDictionary.Add(header.ToLower(), headerIndex);
                        headerIndex++;
                    }

                    List<string> productIdsToExport = new List<string>();
                    GetCategoryTreeResponse[] categoryTree = await this.GetCategoryTree(10, accountName);
                    Dictionary<long, string> categoryIds = await GetCategoryId(categoryTree);
                    if (!string.IsNullOrEmpty(query))
                    {
                        string[] queryArr = query.Split(':');
                        string queryType = queryArr[0];
                        string queryParam = queryArr[1];
                        if (queryType.ToLower().Equals("all"))
                        {
                            foreach (long categoryId in categoryIds.Keys)
                            {
                                ProductAndSkuIdsResponse productAndSkuIdsResponse = await GetProductAndSkuIds(categoryId);
                                if (productAndSkuIdsResponse.Range.Total > 0)
                                {
                                    foreach (KeyValuePair<string, long[]> productSku in productAndSkuIdsResponse.Data)
                                    {
                                        productIdsToExport.Add(productSku.Key);
                                    }
                                }
                            }
                        }
                        else if (queryType.ToLower().Equals("category"))
                        {
                            categoryIds = categoryIds.Where(c => c.Value.Contains(queryParam, StringComparison.OrdinalIgnoreCase)).ToDictionary(c => c.Key, c => c.Value);
                            foreach (long categoryId in categoryIds.Keys)
                            {
                                ProductAndSkuIdsResponse productAndSkuIdsResponse = await GetProductAndSkuIds(categoryId);
                                if (productAndSkuIdsResponse.Range.Total > 0)
                                {
                                    foreach (KeyValuePair<string, long[]> productSku in productAndSkuIdsResponse.Data)
                                    {
                                        productIdsToExport.Add(productSku.Key);
                                    }
                                }
                            }
                        }
                        else if (queryType.ToLower().Equals("brand"))
                        {
                            GetBrandListResponse[] brandList = await GetBrandList(string.Empty);
                        }
                        else if (queryType.ToLower().Equals("productid"))
                        {
                            string[] productIds = queryParam.Split(',');
                            foreach (string id in productIds)
                            {
                                productIdsToExport.Add(id);
                            }
                        }
                        else if (queryType.ToLower().Equals("product"))
                        {
                            ProductSearchResponse[] productSearchResponses = await this.ProductSearch(queryParam);
                            if (productSearchResponses != null)
                            {
                                foreach (ProductSearchResponse productSearchResponse in productSearchResponses)
                                {
                                    productIdsToExport.Add(productSearchResponse.ProductId);
                                }
                            }
                        }
                    }

                    if (productIdsToExport.Count > 0)
                    {
                        long index = 0;
                        long offset = 0;
                        foreach (string productId in productIdsToExport)
                        {
                            GetProductByIdResponse getProductByIdResponse = await GetProductById(productId);
                            List<ProductSkusResponse> productSkusResponses = await GetSkusFromProductId(productId);
                            if (productSkusResponses != null)
                            {
                                foreach (ProductSkusResponse productSkusResponse in productSkusResponses)
                                {
                                    SkuAndContextResponse skuAndContextResponse = null;
                                    try
                                    {
                                        skuAndContextResponse = await GetSkuAndContext(productSkusResponse.Id);
                                    }
                                    catch (Exception ex)
                                    {
                                        _context.Vtex.Logger.Error("ExportToSheet", "GetSkuAndContext", $"Error getting Sku and Context for skuId {productSkusResponse.Id}", ex);
                                    }

                                    GetPriceResponse getPriceResponse = await GetPrice(skuAndContextResponse.Id.ToString());
                                    StringBuilder prodSpecs = new StringBuilder();
                                    ProductSpecification[] productSpecifications = await GetProductSpecifications(productId);
                                    if (productSpecifications != null)
                                    {
                                        foreach (ProductSpecification productSpecification in productSpecifications)
                                        {
                                            prodSpecs.AppendLine($"{productSpecification.Name}:{string.Join(',', productSpecification.Value)}");
                                        }
                                    }

                                    StringBuilder skuSpecs = new StringBuilder();
                                    SkuSpecification[] skuSpecifications = await GetSkuSpecifications(productSkusResponse.Id);
                                    if (skuSpecifications != null)
                                    {
                                        foreach (SkuSpecification skuSpecification in skuSpecifications)
                                        {
                                            string skuSpecName = productSpecifications.Where(s => s.Id.Equals(skuSpecification.FieldValueId)).Select(s => s.Name).FirstOrDefault();
                                            string skuSpecValue = skuSpecification.Text;
                                            skuSpecs.AppendLine($"{skuSpecName}:{skuSpecValue}");
                                        }
                                    }

                                    arrayToWrite[index] = new string[headerIndexDictionary.Count];
                                    arrayToWrite[index][headerIndexDictionary["productid"]] = productId;
                                    arrayToWrite[index][headerIndexDictionary["skuid"]] = productSkusResponse.Id;
                                    arrayToWrite[index][headerIndexDictionary["category"]] = categoryIds[getProductByIdResponse.CategoryId];
                                    arrayToWrite[index][headerIndexDictionary["brand"]] = skuAndContextResponse.BrandName;
                                    arrayToWrite[index][headerIndexDictionary["productname"]] = getProductByIdResponse.Name;
                                    arrayToWrite[index][headerIndexDictionary["product reference code"]] = getProductByIdResponse.RefId;
                                    arrayToWrite[index][headerIndexDictionary["skuname"]] = skuAndContextResponse.NameComplete;
                                    arrayToWrite[index][headerIndexDictionary["sku ean/gtin"]] = skuAndContextResponse.AlternateIds.Ean;
                                    arrayToWrite[index][headerIndexDictionary["sku reference code"]] = skuAndContextResponse.AlternateIds.RefId;
                                    arrayToWrite[index][headerIndexDictionary["height"]] = skuAndContextResponse.Dimension.Height.ToString();
                                    arrayToWrite[index][headerIndexDictionary["width"]] = skuAndContextResponse.Dimension.Width.ToString();
                                    arrayToWrite[index][headerIndexDictionary["length"]] = skuAndContextResponse.Dimension.Length.ToString();
                                    arrayToWrite[index][headerIndexDictionary["weight"]] = skuAndContextResponse.Dimension.Weight.ToString();
                                    arrayToWrite[index][headerIndexDictionary["product description"]] = getProductByIdResponse.DescriptionShort;
                                    arrayToWrite[index][headerIndexDictionary["search keywords"]] = skuAndContextResponse.KeyWords;
                                    arrayToWrite[index][headerIndexDictionary["metatag description"]] = getProductByIdResponse.Description;
                                    arrayToWrite[index][headerIndexDictionary["image url 1"]] = skuAndContextResponse.ImageUrl;
                                    arrayToWrite[index][headerIndexDictionary["display if out of stock"]] = getProductByIdResponse.ShowWithoutStock.ToString().ToUpper();
                                    arrayToWrite[index][headerIndexDictionary["msrp"]] = getPriceResponse != null ? getPriceResponse.CostPrice.ToString() : string.Empty;
                                    arrayToWrite[index][headerIndexDictionary["selling price (price to gpp)"]] = getPriceResponse != null ? getPriceResponse.BasePrice.ToString() : string.Empty;
                                    arrayToWrite[index][headerIndexDictionary["productspecs"]] = prodSpecs.ToString();
                                    arrayToWrite[index][headerIndexDictionary["sku specs"]] = skuSpecs.ToString();


                                    index++;
                                    if (index % writeBlockSize == 0)
                                    {
                                        ValueRange valueRangeToWrite = new ValueRange
                                        {
                                            Range = $"{sheetName}!A{offset + 2}:AZ{offset + writeBlockSize + 1}",
                                            Values = arrayToWrite
                                        };

                                        await _googleSheetsService.WriteSpreadsheetValues(sheetId, valueRangeToWrite);
                                        offset += writeBlockSize;
                                        arrayToWrite = new string[writeBlockSize + 1][];
                                        index = 0;
                                    }
                                }
                            }
                        }

                        ValueRange valueRangeToWriteRemaining = new ValueRange
                        {
                            Range = $"{sheetName}!A{offset + 2}:AZ{offset + writeBlockSize + 1}",
                            Values = arrayToWrite
                        };

                        await _googleSheetsService.WriteSpreadsheetValues(sheetId, valueRangeToWriteRemaining);
                    }
                }
            }

            return "Done";
        }

        private async Task<long?> GetCategoryIdByName(GetCategoryTreeResponse[] categoryTree, string categoryName)
        {
            long? categoryId = null;
            if (!string.IsNullOrEmpty(categoryName))
            {
                string[] nameArr = categoryName.Split('/');
                string currentLevelCategoryName = nameArr[0];
                GetCategoryTreeResponse currentLevelCategoryTree = categoryTree.FirstOrDefault(t => t.Name.Equals(currentLevelCategoryName));
                if (currentLevelCategoryTree != null)
                {
                    if (nameArr.Length == 0)
                    {
                        categoryId = currentLevelCategoryTree.Id;
                    }
                    else if (currentLevelCategoryTree.HasChildren)
                    {
                        categoryName = categoryName.Replace($"{currentLevelCategoryName}/", string.Empty);
                        categoryId = await GetCategoryIdByName(currentLevelCategoryTree.Children, categoryName);
                    }
                }
            }

            return categoryId;
        }

        private async Task<Dictionary<long, string>> GetCategoryId(GetCategoryTreeResponse[] categoryTree)
        {
            Dictionary<long, string> categoryPath = new Dictionary<long, string>();
            foreach (GetCategoryTreeResponse categoryObj in categoryTree)
            {
                if (categoryObj.HasChildren)
                {
                    Dictionary<long, string> childCategoryPath = await GetCategoryId(categoryObj.Children);
                    foreach (long categoryId in childCategoryPath.Keys)
                    {
                        categoryPath.Add(categoryId, $"{categoryObj.Name}/{childCategoryPath[categoryId]}");
                    }
                }
                else
                {
                    categoryPath.Add(categoryObj.Id, categoryObj.Name);
                }
            }

            return categoryPath;
        }

        public async Task<UpdateResponse> CreateProduct(ProductRequest createProductRequest)
        {
            // POST https://{accountName}.{environment}.com.br/api/catalog/pvt/product

            string responseContent = string.Empty;
            bool success = false;
            string statusCode = string.Empty;

            try
            {
                string jsonSerializedData = JsonConvert.SerializeObject(createProductRequest);

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/catalog/pvt/product"),
                    Content = new StringContent(jsonSerializedData, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);

                success = response.IsSuccessStatusCode;
                statusCode = response.StatusCode.ToString();
                responseContent = await response.Content.ReadAsStringAsync();
                _context.Vtex.Logger.Debug("CreateProduct", null, $"{jsonSerializedData} [{statusCode}] {responseContent}");
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("CreateProduct", null, $"Error creating product {createProductRequest.Title}", ex);
            }

            UpdateResponse updateResponse = new UpdateResponse
            {
                Success = success,
                Message = responseContent,
                StatusCode = statusCode
            };

            return updateResponse;
        }

        public async Task<UpdateResponse> UpdateProduct(string productId, ProductRequest updateProductRequest)
        {
            // PUT https://{accountName}.{environment}.com.br/api/catalog/pvt/product/productId

            string responseContent = string.Empty;
            bool success = false;
            string statusCode = string.Empty;

            try
            {
                string jsonSerializedData = JsonConvert.SerializeObject(updateProductRequest);

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Put,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/catalog/pvt/product/{productId}"),
                    Content = new StringContent(jsonSerializedData, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);

                success = response.IsSuccessStatusCode;
                statusCode = response.StatusCode.ToString();
                responseContent = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("UpdateProduct", null, $"Error updating product {updateProductRequest.Title}", ex);
            }

            UpdateResponse updateResponse = new UpdateResponse
            {
                Success = success,
                Message = responseContent,
                StatusCode = statusCode
            };

            return updateResponse;
        }

        public async Task<UpdateResponse> CreateSku(SkuRequest createSkuRequest)
        {
            // POST https://{accountName}.{environment}.com.br/api/catalog/pvt/stockkeepingunit

            string responseContent = string.Empty;
            bool success = false;
            string statusCode = string.Empty;

            try
            {
                string jsonSerializedData = JsonConvert.SerializeObject(createSkuRequest);

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/catalog/pvt/stockkeepingunit"),
                    Content = new StringContent(jsonSerializedData, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);

                success = response.IsSuccessStatusCode;
                statusCode = response.StatusCode.ToString();
                responseContent = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("CreateSku", null, $"Error creating sku {createSkuRequest.Name}", ex);
            }

            UpdateResponse updateResponse = new UpdateResponse
            {
                Success = success,
                Message = responseContent,
                StatusCode = statusCode
            };

            return updateResponse;
        }

        public async Task<UpdateResponse> UpdateSku(string skuId, SkuRequest updateSkuRequest)
        {
            // PUT https://{accountName}.{environment}.com.br/api/catalog/pvt/stockkeepingunit/skuId

            string responseContent = string.Empty;
            bool success = false;
            string statusCode = string.Empty;

            try
            {
                string jsonSerializedData = JsonConvert.SerializeObject(updateSkuRequest);

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Put,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/catalog/pvt/stockkeepingunit/{skuId}"),
                    Content = new StringContent(jsonSerializedData, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);

                success = response.IsSuccessStatusCode;
                statusCode = response.StatusCode.ToString();
                responseContent = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("UpdateSku", null, $"Error updating sku {updateSkuRequest.Name}", ex);
            }

            UpdateResponse updateResponse = new UpdateResponse
            {
                Success = success,
                Message = responseContent,
                StatusCode = statusCode
            };

            return updateResponse;
        }

        public async Task<GetCategoryTreeResponse[]> GetCategoryTree(int categoryLevels, string accountName)
        {
            // GET https://{accountName}.{environment}.com.br/api/catalog_system/pub/category/tree/categoryLevels

            GetCategoryTreeResponse[] getCategoryTreeResponse = null;

            try
            {
                if (string.IsNullOrEmpty(accountName))
                {
                    accountName = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME];
                }

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://portal.vtexcommercestable.com.br/api/catalog_system/pub/category/tree/{categoryLevels}?an={accountName}")

                };

                request.Headers.Add(SheetsCatalogImportConstants.USE_HTTPS_HEADER_NAME, "true");
                string authToken = _context.Vtex.AdminUserAuthToken;
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
                    getCategoryTreeResponse = JsonConvert.DeserializeObject<GetCategoryTreeResponse[]>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetCategoryTree", null, $"Could not get category tree '{categoryLevels}' [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("GetCategoryTree", null, $"Error getting category tree '{categoryLevels}'", ex);
            }

            return getCategoryTreeResponse;
        }

        public async Task<CategoryResponse> CreateCategory(CategoryRequest createCategoryRequest)
        {
            // POST https://{accountName}.{environment}.com.br/api/catalog/pvt/category

            string responseContent = string.Empty;
            CategoryResponse createCategoryResponse = null;

            try
            {
                string jsonSerializedData = JsonConvert.SerializeObject(createCategoryRequest);

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/catalog/pvt/category"),
                    Content = new StringContent(jsonSerializedData, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);

                responseContent = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    createCategoryResponse = JsonConvert.DeserializeObject<CategoryResponse>(responseContent);
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("CreateCategory", null, $"Error creating category {createCategoryRequest.Title}", ex);
            }

            return createCategoryResponse;
        }

        public async Task<GetBrandListResponse[]> GetBrandList(string accountName)
        {
            // GET https://{accountName}.{environment}.com.br/api/catalog_system/pvt/brand/list

            GetBrandListResponse[] getBrandListResponse = null;

            try
            {
                if(string.IsNullOrEmpty(accountName))
                {
                    accountName = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME];
                }

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://portal.vtexcommercestable.com.br/api/catalog_system/pvt/brand/list?an={accountName}")
                };

                request.Headers.Add(SheetsCatalogImportConstants.USE_HTTPS_HEADER_NAME, "true");
                string authToken = _context.Vtex.AdminUserAuthToken;
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"GetBrandList [{response.StatusCode}] {accountName}");
                if (response.IsSuccessStatusCode)
                {
                    getBrandListResponse = JsonConvert.DeserializeObject<GetBrandListResponse[]>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetBrandList", null, $"Could not get brand list [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("GetBrandList", null, $"Error getting brand list", ex);
            }

            return getBrandListResponse;
        }

        public async Task<GetBrandListV2Response> GetBrandListV2(string accountName)
        {
            // GET https://{accountName}.{environment}.com.br/api/catalogv2/brands
            // portal.vtexcommercestable.com.br/api/whatever?an={accountName}

            GetBrandListV2Response getBrandListResponse = null;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://portal.vtexcommercestable.com.br/api/catalogv2/brands?an={accountName}")
                };

                request.Headers.Add(SheetsCatalogImportConstants.USE_HTTPS_HEADER_NAME, "true");
                string authToken = _context.Vtex.AdminUserAuthToken;
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"GetBrandListV2 {accountName} {response.StatusCode} {responseContent}");
                if (response.IsSuccessStatusCode)
                {
                    getBrandListResponse = JsonConvert.DeserializeObject<GetBrandListV2Response>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetBrandListV2", null, $"Could not get brand list '{accountName}' [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("GetBrandListV2", null, $"Error getting brand list '{accountName}'", ex);
            }

            return getBrandListResponse;
        }

        public async Task<CreateBrandV2Response> CreateBrandV2(string brandName)
        {
            CreateBrandV2Response createBrandV2Response = null;
            CreateBrandV2Request createBrandV2Request = new CreateBrandV2Request
            {
                Name = brandName,
                IsActive = true
            };

            try
            {
                string accountName = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME];
                string jsonSerializedData = JsonConvert.SerializeObject(createBrandV2Request);

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri($"http://portal.vtexcommercestable.com.br/api/catalogv2/brands?an={accountName}"),
                    Content = new StringContent(jsonSerializedData, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                request.Headers.Add(SheetsCatalogImportConstants.USE_HTTPS_HEADER_NAME, "true");
                string authToken = _context.Vtex.AdminUserAuthToken;
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"CreateBrandV2 {accountName} {response.StatusCode} {responseContent}");
                if (response.IsSuccessStatusCode)
                {
                    createBrandV2Response = JsonConvert.DeserializeObject<CreateBrandV2Response>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("CreateBrandV2", null, $"Could not create brand '{brandName}' [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("CreateBrandV2", null, $"Error creating brand '{brandName}'", ex);
            }

            return createBrandV2Response;
        }

        public async Task<GetCategoryListV2Response> GetCategoryListV2(string accountName)
        {
            // GET https://{accountName}.{environment}.com.br/api/catalogv2/brands

            GetCategoryListV2Response getCategoryList = null;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://portal.vtexcommercestable.com.br/api/catalogv2/category-tree?an={accountName}")
                };

                request.Headers.Add(SheetsCatalogImportConstants.USE_HTTPS_HEADER_NAME, "true");
                string authToken = _context.Vtex.AdminUserAuthToken;
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"GetCategoryListV2 {accountName}: {response.StatusCode}");
                if (response.IsSuccessStatusCode)
                {
                    getCategoryList = JsonConvert.DeserializeObject<GetCategoryListV2Response>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetCategoryListV2", null, $"Could not get category list '{accountName}' [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetCategoryListV2 Error getting category list '{accountName}'");
                _context.Vtex.Logger.Error("GetCategoryListV2", null, $"Error getting category list '{accountName}'", ex);
            }

            return getCategoryList;
        }

        public async Task<CreateCategoryV2Response> CreateCategoryV2(string categoryName)
        {
            CreateCategoryV2Response createCategoryV2Response = null;
            CreateCategoryV2Request createCategoryV2Request = new CreateCategoryV2Request
            {
                Name = categoryName
            };

            try
            {
                string accountName = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME];
                string jsonSerializedData = JsonConvert.SerializeObject(createCategoryV2Request);

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri($"http://portal.vtexcommercestable.com.br/api/catalogv2/category-tree/categories?an={accountName}"),
                    Content = new StringContent(jsonSerializedData, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                request.Headers.Add(SheetsCatalogImportConstants.USE_HTTPS_HEADER_NAME, "true");
                string authToken = _context.Vtex.AdminUserAuthToken;
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"CreateCategoryV2 {accountName} {response.StatusCode} {responseContent}");
                if (response.IsSuccessStatusCode)
                {
                    createCategoryV2Response = JsonConvert.DeserializeObject<CreateCategoryV2Response>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("CreateCategoryV2", null, $"Could not create brand '{categoryName}' [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("CreateCategoryV2", null, $"Error creating brand '{categoryName}'", ex);
            }

            return createCategoryV2Response;
        }

        public async Task<GetProductByIdResponse> GetProductById(string productId)
        {
            // GET https://{accountName}.{environment}.com.br/api/catalog/pvt/product/productId

            GetProductByIdResponse getProductByIdResponse = null;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/catalog/pvt/product/{productId}")
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
                    getProductByIdResponse = JsonConvert.DeserializeObject<GetProductByIdResponse>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetProductById", null, $"Could not get product for id '{productId}' [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("GetProductById", null, $"Error getting product for id '{productId}'", ex);
            }

            return getProductByIdResponse;
        }

        public async Task<long[]> ListSkuIds(int page, int pagesize)
        {
            // GET https://{accountName}.{environment}.com.br/api/catalog_system/pvt/sku/stockkeepingunitids

            long[] listSkuIdsResponse = null;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/catalog_system/pvt/sku/stockkeepingunitids?page={page}&pagesize={pagesize}")
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
                    listSkuIdsResponse = JsonConvert.DeserializeObject<long[]>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("ListSkuIds", null, $"Could not get sku ids {page}/{pagesize} [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("ListSkuIds", null, $"Error getting sku ids {page}/{pagesize}", ex);
            }

            return listSkuIdsResponse;
        }

        public async Task<UpdateResponse> CreateSkuFile(string skuId, string imageName, string imageText, bool isMain, string imageUrl)
        {
            //POST https://{accountName}.{environment}.com.br/api/catalog/pvt/stockkeepingunit/skuId/file

            bool success = false;
            string responseContent = string.Empty;
            string statusCode = string.Empty;

            if (string.IsNullOrEmpty(skuId) || string.IsNullOrEmpty(imageUrl))
            {
                responseContent = "Missing Parameter";
            }
            else
            {
                try
                {
                    ImageUpdate imageUpdate = new ImageUpdate
                    {
                        IsMain = isMain,
                        Label = imageText,
                        Name = imageName,
                        Text = imageText,
                        Url = imageUrl
                    };

                    string jsonSerializedData = JsonConvert.SerializeObject(imageUpdate);

                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Post,
                        RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/catalog/pvt/stockkeepingunit/{skuId}/file"),
                        Content = new StringContent(jsonSerializedData, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                    };

                    string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                    if (authToken != null)
                    {
                        request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                        request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                        request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                    }

                    var client = _clientFactory.CreateClient();
                    var response = await client.SendAsync(request);
                    responseContent = await response.Content.ReadAsStringAsync();
                    success = response.IsSuccessStatusCode;
                    if (!success)
                    {
                        _context.Vtex.Logger.Info("UpdateSkuImage", null, $"Response: {responseContent}  [{response.StatusCode}] for request '{jsonSerializedData}' to {request.RequestUri}");
                    }

                    statusCode = response.StatusCode.ToString();
                    if (string.IsNullOrEmpty(responseContent))
                    {
                        responseContent = $"Updated:{success} {response.StatusCode}";
                    }
                    else if (responseContent.Contains(SheetsCatalogImportConstants.ARCHIVE_CREATED))
                    {
                        // If the image was already added to the sku, consider it a success
                        success = true;
                    }
                }
                catch (Exception ex)
                {
                    _context.Vtex.Logger.Error("UpdateSkuImage", null, $"Error updating sku '{skuId}' {imageName}", ex);
                    success = false;
                    responseContent = ex.Message;
                }
            }

            UpdateResponse updateResponse = new UpdateResponse
            {
                Success = success,
                Message = responseContent,
                StatusCode = statusCode
            };

            return updateResponse;
        }

        public async Task<UpdateResponse> CreateEANGTIN(string skuId, string ean)
        {
            // POST https://accountName.vtexcommercestable.com.br/api/catalog/pvt/stockkeepingunit/skuId/ean/ean

            bool success = false;
            string responseContent = string.Empty;
            string statusCode = string.Empty;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/catalog/pvt/stockkeepingunit/{skuId}/ean/{ean}"),
                };

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);

                success = response.IsSuccessStatusCode;
                statusCode = response.StatusCode.ToString();
                responseContent = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("CreateEANGTIN", null, $"Error creating EAN/GTIN {ean} for Sku {skuId}", ex);
            }

            UpdateResponse updateResponse = new UpdateResponse
            {
                Success = success,
                Message = responseContent,
                StatusCode = statusCode
            };

            return updateResponse;
        }

        public async Task<UpdateResponse> CreatePrice(string skuId, CreatePrice createPrice)
        {
            // PUT https://api.vtex.com/accountName/pricing/prices/skuId

            bool success = false;
            string responseContent = string.Empty;
            string statusCode = string.Empty;

            try
            {
                string jsonSerializedData = JsonConvert.SerializeObject(createPrice);

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Put,
                    RequestUri = new Uri($"http://api.vtex.com/{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}/pricing/prices/{skuId}"),
                    Content = new StringContent(jsonSerializedData, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);

                success = response.IsSuccessStatusCode;
                statusCode = response.StatusCode.ToString();
                responseContent = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("CreatePrice", null, $"Error creating Price for Sku {skuId}", ex);
            }

            UpdateResponse updateResponse = new UpdateResponse
            {
                Success = success,
                Message = responseContent,
                StatusCode = statusCode
            };

            return updateResponse;
        }

        public async Task<GetWarehousesResponse[]> GetWarehouses()
        {
            // GET https://logistics.environment.com.br/api/logistics/pvt/configuration/warehouses?an=accountName

            GetWarehousesResponse[] getWarehousesResponse = null;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://logistics.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/logistics/pvt/configuration/warehouses?an={this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}")
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
                    getWarehousesResponse = JsonConvert.DeserializeObject<GetWarehousesResponse[]>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetWarehouses", null, $"Could not get warehouses' [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("GetWarehouses", null, $"Error getting warehouses", ex);
            }

            return getWarehousesResponse;
        }

        public async Task<GetWarehousesResponse[]> ListAllWarehouses()
        {
            GetWarehousesResponse[] listAllWarehousesResponse = null;
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.vtexcommercestable.com.br/api/logistics/pvt/configuration/warehouses")
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
                listAllWarehousesResponse = JsonConvert.DeserializeObject<GetWarehousesResponse[]>(responseContent);
            }

            return listAllWarehousesResponse;
        }

        public async Task<UpdateResponse> SetInventory(string skuId, string warehouseId, InventoryRequest inventoryRequest)
        {
            // PUT https://logistics.vtexcommercestable.com.br/api/logistics/pvt/inventory/skus/skuId/warehouses/warehouseId?an=accountName

            bool success = false;
            string responseContent = string.Empty;
            string statusCode = string.Empty;

            try
            {
                string jsonSerializedData = JsonConvert.SerializeObject(inventoryRequest);

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Put,
                    RequestUri = new Uri($"http://logistics.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/logistics/pvt/inventory/skus/{skuId}/warehouses/{warehouseId}?an={this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}"),
                    Content = new StringContent(jsonSerializedData, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);

                success = response.IsSuccessStatusCode;
                statusCode = response.StatusCode.ToString();
                responseContent = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("SetInventory", null, $"Error setting inventory for sku {skuId} in warehouse {warehouseId}", ex);
            }

            UpdateResponse updateResponse = new UpdateResponse
            {
                Success = success,
                Message = responseContent,
                StatusCode = statusCode
            };

            return updateResponse;
        }

        public async Task<UpdateResponse> SetProdSpecs(string productId, SpecAttr prodSpec)
        {
            // PUT http://accountName.environment.com.br/api/catalog/pvt/product/productId/specificationvalue

            bool success = false;
            string responseContent = string.Empty;
            string statusCode = string.Empty;

            try
            {
                string jsonSerializedData = JsonConvert.SerializeObject(prodSpec);

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Put,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.vtexcommercestable.com.br/api/catalog/pvt/product/{productId}/specificationvalue"),
                    Content = new StringContent(jsonSerializedData, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);

                success = response.IsSuccessStatusCode;
                statusCode = response.StatusCode.ToString();
                responseContent = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("SetProdSpecs", null, $"Error setting product specs for prodId {productId}", ex);
            }

            UpdateResponse updateResponse = new UpdateResponse
            {
                Success = success,
                Message = responseContent,
                StatusCode = statusCode
            };

            return updateResponse;
        }

        public async Task<UpdateResponse> SetSkuSpec(string skuId, SpecAttr skuSpec)
        {
            // PUT http://accountName.vtexcommercestable.com.br/api/catalog/pvt/stockkeepingunit/SkuId/specificationvalue

            bool success = false;
            string responseContent = string.Empty;
            string statusCode = string.Empty;

            try
            {
                string jsonSerializedData = JsonConvert.SerializeObject(skuSpec);
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Put,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.vtexcommercestable.com.br/api/catalog/pvt/stockkeepingunit/{skuId}/specificationvalue"),
                    Content = new StringContent(jsonSerializedData, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);

                success = response.IsSuccessStatusCode;
                statusCode = response.StatusCode.ToString();
                responseContent = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("SetSkuSpec", null, $"Error setting product specs for sku {skuId}", ex);
            }

            UpdateResponse updateResponse = new UpdateResponse
            {
                Success = success,
                Message = responseContent,
                StatusCode = statusCode
            };

            return updateResponse;
        }

        public async Task<ProductAndSkuIdsResponse> GetProductAndSkuIds(long categoryId)
        {
            // GET https://{accountName}.{environment}.com.br/api/catalog_system/pvt/products/GetProductAndSkuIds

            ProductAndSkuIdsResponse productAndSkuIdsResponse = new ProductAndSkuIdsResponse();

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.vtexcommercestable.com.br/api/catalog_system/pvt/products/GetProductAndSkuIds?categoryId={categoryId}")
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
                    productAndSkuIdsResponse = JsonConvert.DeserializeObject<ProductAndSkuIdsResponse>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetProductAndSkuIds", null, $"Could not get products and skus for category '{categoryId}' [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("GetProductAndSkuIds", null, $"Error getting products and skus for category '{categoryId}'", ex);
            }

            return productAndSkuIdsResponse;
        }

        public async Task<SkuAndContextResponse> GetSkuAndContext(string skuId)
        {
            // GET https://{accountName}.{environment}.com.br/api/catalog_system/pvt/sku/stockkeepingunitbyid/skuId

            SkuAndContextResponse skuAndContextResponse = new SkuAndContextResponse();

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.vtexcommercestable.com.br/api/catalog_system/pvt/sku/stockkeepingunitbyid/{skuId}")
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
                    skuAndContextResponse = JsonConvert.DeserializeObject<SkuAndContextResponse>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetSkuAndContext", null, $"Could not get sku '{skuId}' [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("GetSkuAndContext", null, $"Error getting sku '{skuId}'", ex);
            }

            return skuAndContextResponse;
        }

        public async Task<string[]> GetEansBySkuId(long skuId)
        {
            // GET https://{accountName}.{environment}.com.br/api/catalog/pvt/stockkeepingunit/skuId/ean

            string[] eans = null;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.vtexcommercestable.com.br/api/catalog/pvt/stockkeepingunit/{skuId}/ean")
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
                    eans = JsonConvert.DeserializeObject<string[]>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetEanBySkuId", null, $"Could not get EAN for sku '{skuId}' [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("GetEanBySkuId", null, $"Error getting EAN for sku '{skuId}'", ex);
            }

            return eans;
        }

        public async Task<ProductSearchResponse[]> ProductSearch(string search)
        {
            // GET https://{accountName}.{environment}.com.br/api/catalog_system/pub/products/search/search

            ProductSearchResponse[] productSearchResponse = null;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.vtexcommercestable.com.br/api/catalog_system/pub/products/search/{search}")
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
                    productSearchResponse = JsonConvert.DeserializeObject<ProductSearchResponse[]>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("ProductSearch", null, $"Could not search '{search}' [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("ProductSearch", null, $"Error searching '{search}'", ex);
            }

            return productSearchResponse;
        }

        public async Task<List<ProductSkusResponse>> GetSkusFromProductId(string productId)
        {
            // GET https://{{accountName}}.vtexcommercestable.com.br/api/catalog_system/pvt/sku/stockkeepingunitByProductId/{{productId}}

            List<ProductSkusResponse> productSkusResponses = null;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/catalog_system/pvt/sku/stockkeepingunitByProductId/{productId}")
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
                    productSkusResponses = JsonConvert.DeserializeObject<List<ProductSkusResponse>>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetSkusFromProductId", null, $"Could not get skus for product id '{productId}'  [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("GetSkusFromProductId", null, $"Error getting skus for product id '{productId}'", ex);
            }

            return productSkusResponses;
        }

        public async Task<UpdateResponse> ExtraInfo(ExtraInfo extraInfo)
        {
            // PUT https://accountName.vtexcommercestable.com.br/api/catalogv2/products/{productId}/extra-info

            bool success = false;
            string responseContent = string.Empty;
            string statusCode = string.Empty;
            string productId = extraInfo.ProductId;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Put,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/catalogv2/products/{productId}/extra-info"),
                };

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);

                success = response.IsSuccessStatusCode;
                statusCode = response.StatusCode.ToString();
                responseContent = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("ExtraInfo", null, $"Error setting ExtraInfo for Product Id '{productId}'", ex);
            }

            UpdateResponse updateResponse = new UpdateResponse
            {
                Success = success,
                Message = responseContent,
                StatusCode = statusCode
            };

            return updateResponse;
        }

        public async Task<UpdateResponse> CreateProductToTradePolicy(string productId, string tradepolicyId)
        {
            bool success = false;
            string responseContent = string.Empty;
            string statusCode = string.Empty;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/catalog/pvt/product/{productId}/salespolicy/{tradepolicyId}"),
                };

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);

                success = response.IsSuccessStatusCode;
                statusCode = response.StatusCode.ToString();
                responseContent = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("CreateProductToTradePolicy", null, $"Error creating Trade Policy {tradepolicyId} for Product Id '{productId}'", ex);
            }

            UpdateResponse updateResponse = new UpdateResponse
            {
                Success = success,
                Message = responseContent,
                StatusCode = statusCode
            };

            return updateResponse;
        }

        public async Task<string> ClearSheet()
        {
            string response = string.Empty;

            DateTime importStarted = await _sheetsCatalogImportRepository.CheckImportLock();
            TimeSpan elapsedTime = DateTime.Now - importStarted;
            if (elapsedTime.TotalHours < SheetsCatalogImportConstants.LOCK_TIMEOUT)
            {
                _context.Vtex.Logger.Info("ClearSheet", null, $"Blocked by lock.  Import started: {importStarted}");
                return ($"Import started {importStarted} in progress.");
            }

            string importFolderId = null;
            string accountName = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME];

            FolderIds folderIds = await _sheetsCatalogImportRepository.LoadFolderIds(accountName);
            if (folderIds != null)
            {
                importFolderId = folderIds.ProductsFolderId;
            }

            ListFilesResponse spreadsheets = await _googleSheetsService.ListSheetsInFolder(importFolderId);
            if (spreadsheets != null)
            {
                var sheetIds = spreadsheets.Files.Select(s => s.Id);
                foreach (var sheetId in sheetIds)
                {
                    SheetRange sheetRange = new SheetRange();
                    sheetRange.Ranges = new List<string>();
                    sheetRange.Ranges.Add($"A2:ZZ{SheetsCatalogImportConstants.DEFAULT_SHEET_SIZE}");
                    response = await _googleSheetsService.ClearSpreadsheet(sheetId, sheetRange);
                }
            }

            _context.Vtex.Logger.Info("ClearSheet", null, $"Sheet Cleared: '{response}'");

            return response;
        }

        public async Task<ListFilesResponse> ListImageFiles()
        {
            // GET https://{{accountName}}.myvtex.com/google-drive-import/list-images

            ListFilesResponse listFilesResponse = null;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.myvtex.com/google-drive-import/list-images")
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
                    listFilesResponse = JsonConvert.DeserializeObject<ListFilesResponse>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("ListImageFiles", null, $"Could not get image file list  [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("ListImageFiles", null, $"Error getting image file list", ex);
            }

            return listFilesResponse;
        }

        public async Task<string> AddImagesToSheet()
        {
            string response = string.Empty;
            string importFolderId = null;
            string accountName = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME];

            FolderIds folderIds = await _sheetsCatalogImportRepository.LoadFolderIds(accountName);
            if (folderIds != null)
            {
                importFolderId = folderIds.ProductsFolderId;
            }

            ListFilesResponse spreadsheets = await _googleSheetsService.ListSheetsInFolder(importFolderId);
            if (spreadsheets != null)
            {
                var sheetIds = spreadsheets.Files.Select(s => s.Id);
                foreach (var sheetId in sheetIds)
                {
                    ListFilesResponse listFilesResponse = await this.ListImageFiles();
                    if (listFilesResponse != null)
                    {
                        string[][] filesToWrite = new string[listFilesResponse.Files.Count][];
                        int index = 0;
                        foreach (GoogleFile file in listFilesResponse.Files)
                        {
                            filesToWrite[index] = new string[] { file.Name, $"=IMAGE(\"{ file.ThumbnailLink}\")", file.WebContentLink.ToString() };
                            index++;
                        }

                        ValueRange valueRangeToWrite = new ValueRange
                        {
                            Range = $"{SheetsCatalogImportConstants.SheetNames.IMAGES}!A2:C{listFilesResponse.Files.Count + 1}",
                            Values = filesToWrite
                        };

                        await _googleSheetsService.WriteSpreadsheetValues(sheetId, valueRangeToWrite);
                    }
                }
            }

            return response;
        }

        public async Task<GetSkuImagesResponse[]> GetSkuImages(string skuId)
        {
            // GET https://{accountName}.{environment}.com.br/api/catalog/pvt/stockkeepingunit/skuId/file

            GetSkuImagesResponse[] getSkuResponse = null;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/catalog/pvt/stockkeepingunit/{skuId}/file")
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
                    getSkuResponse = JsonConvert.DeserializeObject<GetSkuImagesResponse[]>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetSkuImages", null, $"Did not get images for skuid '{skuId}'");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("GetSkuImages", null, $"Error getting images for skuid '{skuId}'", ex);
            }

            return getSkuResponse;
        }

        public async Task<GetPriceResponse> GetPrice(string skuId)
        {
            // GET https://api.vtex.com/{accountName}/pricing/prices/itemId

            GetPriceResponse getPriceResponse = null;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://api.vtex.com/{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}/pricing/prices/{skuId}")
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
                    getPriceResponse = JsonConvert.DeserializeObject<GetPriceResponse>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetPrice", null, $"Could not get prices for sku '{skuId}' [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("GetPrice", null, $"Error getting prices for sku '{skuId}'", ex);
            }

            return getPriceResponse;
        }

        public async Task<ProductSpecification[]> GetProductSpecifications(string productId)
        {
            // GET https://{accountName}.{environment}.com.br/api/catalog_system/pvt/products/productId/specification

            ProductSpecification[] productSpecifications = null;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/catalog_system/pvt/products/{productId}/specification")
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
                    productSpecifications = JsonConvert.DeserializeObject<ProductSpecification[]>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetProductSpecifications", null, $"Could not get product specifications for product id '{productId}' [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("GetProductSpecifications", null, $"Error getting product specifications for product id '{productId}'", ex);
            }

            return productSpecifications;
        }

        public async Task<SkuSpecification[]> GetSkuSpecifications(string skuId)
        {
            // GET https://{accountName}.{environment}.com.br/api/catalog/pvt/stockkeepingunit/skuId/specification

            SkuSpecification[] productSpecifications = null;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/catalog/pvt/stockkeepingunit/{skuId}/specification")
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
                    productSpecifications = JsonConvert.DeserializeObject<SkuSpecification[]>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetSkuSpecifications", null, $"Could not get sku specifications for sku id '{skuId}' [{response.StatusCode}]");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("GetSkuSpecifications", null, $"Error getting sku specifications for sku id '{skuId}'", ex);
            }

            return productSpecifications;
        }

        public async Task<UpdateResponse> CreateProductV2(ProductRequestV2 createProductRequest)
        {
            // POST https://{{environment-catalog}}/api/catalogv2/products?an=

            string responseContent = string.Empty;
            bool success = false;
            string statusCode = string.Empty;

            try
            {
                string jsonSerializedData = JsonConvert.SerializeObject(createProductRequest);

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    //RequestUri = new Uri($"http://{SheetsCatalogImportConstants.ENV_CATALOG}/api/catalogv2/products?an={this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}"),
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.vtexcommercestable.com.br/api/catalogv2/products"),
                    Content = new StringContent(jsonSerializedData, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);

                success = response.IsSuccessStatusCode;
                statusCode = response.StatusCode.ToString();
                responseContent = await response.Content.ReadAsStringAsync();
                _context.Vtex.Logger.Debug("CreateProductV2", null, $"Request: '{jsonSerializedData}' \nResponse: [{statusCode}] '{responseContent}'");
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("CreateProductV2", null, $"Error creating product {createProductRequest.Name}", ex);
            }

            UpdateResponse updateResponse = new UpdateResponse
            {
                Success = success,
                Message = responseContent,
                StatusCode = statusCode
            };

            return updateResponse;
        }

        public async Task<UpdateResponse> UpdateProductV2(ProductRequestV2 updateProductRequest)
        {
            // POST https://{{environment-catalog}}/api/catalogv2/products?an=

            string responseContent = string.Empty;
            bool success = false;
            string statusCode = string.Empty;

            try
            {
                string jsonSerializedData = JsonConvert.SerializeObject(updateProductRequest);

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Put,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.vtexcommercestable.com.br/api/catalogv2/products"),
                    Content = new StringContent(jsonSerializedData, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);

                success = response.IsSuccessStatusCode;
                statusCode = response.StatusCode.ToString();
                responseContent = await response.Content.ReadAsStringAsync();
                _context.Vtex.Logger.Debug("UpdateProductV2", null, $"Request: '{jsonSerializedData}' \nResponse: [{statusCode}] '{responseContent}'");
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("UpdateProductV2", null, $"Error updating product {updateProductRequest.Name}", ex);
            }

            UpdateResponse updateResponse = new UpdateResponse
            {
                Success = success,
                Message = responseContent,
                StatusCode = statusCode
            };

            return updateResponse;
        }

        public async Task<UpdateResponse> UpdateProductV2(ProductRequest updateProductRequest)
        {
            // PUT https://{accountName}.{environment}.com.br/api/catalogv2/products

            string responseContent = string.Empty;
            bool success = false;
            string statusCode = string.Empty;

            try
            {
                string jsonSerializedData = JsonConvert.SerializeObject(updateProductRequest);

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Put,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/catalogv2/products"),
                    Content = new StringContent(jsonSerializedData, Encoding.UTF8, SheetsCatalogImportConstants.APPLICATION_JSON)
                };

                string authToken = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.HEADER_VTEX_CREDENTIAL];
                if (authToken != null)
                {
                    request.Headers.Add(SheetsCatalogImportConstants.AUTHORIZATION_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.VTEX_ID_HEADER_NAME, authToken);
                    request.Headers.Add(SheetsCatalogImportConstants.PROXY_AUTHORIZATION_HEADER_NAME, authToken);
                }

                var client = _clientFactory.CreateClient();
                var response = await client.SendAsync(request);

                success = response.IsSuccessStatusCode;
                statusCode = response.StatusCode.ToString();
                responseContent = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("UpdateProduct", null, $"Error updating product {updateProductRequest.Title}", ex);
            }

            UpdateResponse updateResponse = new UpdateResponse
            {
                Success = success,
                Message = responseContent,
                StatusCode = statusCode
            };

            return updateResponse;
        }

        public async Task<ProductResponseV2> GetProductV2(string productId)
        {
            // GET https://{accountName}.{environment}.com.br/api/catalogv2/products/{productId}
            ProductResponseV2 productResponseV2 = null;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"http://{this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}.{SheetsCatalogImportConstants.ENVIRONMENT}.com.br/api/catalogv2/products/{productId}")
                };

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
                    productResponseV2 = JsonConvert.DeserializeObject<ProductResponseV2>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetProductV2", null, $"Could not get product {productId}.\n[{response.StatusCode}] {responseContent}");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("GetProductV2", null, $"Error getting product {productId}", ex);
            }

            return productResponseV2;
        }

        public async Task<ProductResponseV2> GetProductByExternalIdV2(string externalId)
        {
            // GET http://portal.vtexcommercestable.com.br/api/catalogv2/products?an=vyskseller2603&externalid=101
            ProductResponseV2 productResponseV2 = null;

            try
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"https://portal.vtexcommercestable.com.br/api/catalogv2/products?an={this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME]}&externalid={externalId}")
                };

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
                    productResponseV2 = JsonConvert.DeserializeObject<ProductResponseV2>(responseContent);
                }
                else
                {
                    _context.Vtex.Logger.Warn("GetProductByExternalIdV2", null, $"Could not get product {externalId}.\n[{response.StatusCode}] {responseContent}");
                }
            }
            catch (Exception ex)
            {
                _context.Vtex.Logger.Error("GetProductByExternalIdV2", null, $"Error getting product {externalId}", ex);
            }

            return productResponseV2;
        }

        public async Task<bool> SetBrandList()
        {
            bool success = false;
            string sheetId = string.Empty;
            string importFolderId = null;
            string accountName = this._httpContextAccessor.HttpContext.Request.Headers[SheetsCatalogImportConstants.VTEX_ACCOUNT_HEADER_NAME];
            FolderIds folderIds = await _sheetsCatalogImportRepository.LoadFolderIds(accountName);
            if (folderIds != null)
            {
                importFolderId = folderIds.ProductsFolderId;
            }

            ListFilesResponse spreadsheets = await _googleSheetsService.ListSheetsInFolder(importFolderId);
            if (spreadsheets != null)
            {
                sheetId = spreadsheets.Files.Select(s => s.Id).FirstOrDefault();
                string sheetContent = await _googleSheetsService.GetSheet(sheetId, string.Empty);
                GoogleSheet googleSheet = JsonConvert.DeserializeObject<GoogleSheet>(sheetContent);
                string[] sheetHeader = googleSheet.ValueRanges[0].Values[0];
                int headerIndex = 0;
                Dictionary<string, int> headerIndexDictionary = new Dictionary<string, int>();
                foreach (string header in sheetHeader)
                {
                    headerIndexDictionary.Add(header.ToLower(), headerIndex);
                    headerIndex++;
                }

                int brandColumnIndex = headerIndexDictionary["brand"];
                int categoryColumnIndex = headerIndexDictionary["category"];
                bool isCatalogV2 = false;
                bool useCatalogV1 = false;
                AppSettings appSettings = await _sheetsCatalogImportRepository.GetAppSettings();
                if (appSettings != null)
                {
                    isCatalogV2 = appSettings.IsV2Catalog;
                    if(!string.IsNullOrEmpty(appSettings.AccountName))
                    {
                        accountName = appSettings.AccountName;
                        useCatalogV1 = true;    // Assume Marketplace is V1
                    }
                }

                List<string> updateBrandValueList = new List<string>();
                List<string> updateCategoryValueList = new List<string>();

                if (isCatalogV2 && !useCatalogV1)
                {
                    GetBrandListV2Response brandList = await this.GetBrandListV2(accountName);
                    Array.Sort(brandList.Data, delegate (Datum x, Datum y) { return x.Name.CompareTo(y.Name); });
                    foreach (Datum data in brandList.Data)
                    {
                        updateBrandValueList.Add(data.Name);
                    }

                    GetCategoryListV2Response categoryList = await this.GetCategoryListV2(accountName);
                    Array.Sort(categoryList.Roots, delegate (Root x, Root y) { return x.Value.Name.CompareTo(y.Value.Name); });
                    foreach (Root data in categoryList.Roots)
                    {
                        updateCategoryValueList.Add(data.Value.Name);
                    }
                }
                else
                {
                    GetBrandListResponse[] brandLists = await this.GetBrandList(accountName);
                    Array.Sort(brandLists, delegate(GetBrandListResponse x, GetBrandListResponse y) { return x.Name.CompareTo(y.Name); });
                    foreach(GetBrandListResponse brandList in brandLists)
                    {
                        updateBrandValueList.Add(brandList.Name);
                    }

                    GetCategoryTreeResponse[] categoryTree = await this.GetCategoryTree(100, accountName);
                    Dictionary<long, string> categoryList = await GetCategoryId(categoryTree);
                    var sortedList = categoryList.OrderBy(d => d.Value).ToList();
                    foreach(KeyValuePair<long, string> kvp in sortedList)
                    {
                        updateCategoryValueList.Add(kvp.Value);
                    }
                }

                int writeBlockSize = Math.Max(updateBrandValueList.Count, updateCategoryValueList.Count);
                string[][] arrayToWrite = new string[writeBlockSize][];
                try
                {
                    for (int index = 0; index < writeBlockSize; index++)
                    {
                        string categoryName = string.Empty;
                        string brandName = string.Empty;
                        if (updateCategoryValueList.Count > index)
                        {
                            categoryName = updateCategoryValueList[index];
                        }

                        if (updateBrandValueList.Count > index)
                        {
                            brandName = updateBrandValueList[index];
                        }

                        arrayToWrite[index] = new string[] { categoryName, brandName };
                    }
                }
                catch(Exception ex)
                {
                    _context.Vtex.Logger.Error("SetBrandList", null, $"Set Brands and Categories error", ex);
                }

                ValueRange valueRangeToWrite = new ValueRange
                {
                    Range = $"{SheetsCatalogImportConstants.SheetNames.VALIDATION}!A1:B{writeBlockSize}",
                    Values = arrayToWrite
                };

                await _googleSheetsService.WriteSpreadsheetValues(sheetId, valueRangeToWrite);

                BatchUpdate batchUpdate = new BatchUpdate
                {
                    Requests = new Request[]
                    {
                        new Request
                        {
                            SetDataValidation = new SetDataValidation
                            {
                                Range = new BatchUpdateRange
                                {
                                    StartRowIndex = 1,
                                    EndRowIndex = SheetsCatalogImportConstants.DEFAULT_SHEET_SIZE,
                                    SheetId = 0,
                                    EndColumnIndex = categoryColumnIndex + 1,
                                    StartColumnIndex = categoryColumnIndex
                                },
                                Rule = new Rule
                                {
                                    Condition = new Condition
                                    {
                                        Type = "ONE_OF_RANGE",
                                        Values = new Value[]
                                        {
                                            new Value
                                            {
                                                UserEnteredValue = $"={SheetsCatalogImportConstants.SheetNames.VALIDATION}!$A$1:$A${updateCategoryValueList.Count}"
                                            }
                                        }
                                    },
                                    InputMessage = $"Choose Category",
                                    Strict = false
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
                                    EndColumnIndex = brandColumnIndex + 1,
                                    StartColumnIndex = brandColumnIndex
                                },
                                Rule = new Rule
                                {
                                    Condition = new Condition
                                    {
                                        Type = "ONE_OF_RANGE",
                                        Values = new Value[]
                                        {
                                            new Value
                                            {
                                                UserEnteredValue = $"={SheetsCatalogImportConstants.SheetNames.VALIDATION}!$B$1:$B${updateBrandValueList.Count}"
                                            }
                                        }
                                    },
                                    InputMessage = $"Choose Brand",
                                    Strict = false
                                }
                            }
                        }
                    }
                };

                success = await _googleSheetsService.BatchUpdate(sheetId, batchUpdate);
            }
            else
            {
                Console.WriteLine("NULL Sheet");
            }

            _context.Vtex.Logger.Info("SetBrandList", null, $"Set Brands and Categories [{success}] ");

            return success;
        }

        private async Task<double?> ParseDouble(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            else
            {
                double retVal;
                if (double.TryParse(value, out retVal))
                {
                    return retVal;
                }
                else
                {
                    _context.Vtex.Logger.Warn("ParseDouble", null, $"Could not parse {value}");
                    return null;
                }
            }
        }

        private async Task<decimal?> ParseCurrency(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            else
            {
                var regex = new Regex(@"([\d,.]+)");
                var match = regex.Match(value);
                if (match.Success)
                {
                    value = match.Groups[1].Value;
                }

                decimal retVal;
                if (decimal.TryParse(value, out retVal))
                {
                    return retVal;
                }
                else
                {
                    _context.Vtex.Logger.Warn("ParseCurrency", null, $"Could not parse {value}");
                    return null;
                }
            }
        }

        private async Task<long?> ParseLong(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            else
            {
                long retVal;
                if (long.TryParse(value, out retVal))
                {
                    return retVal;
                }
                else
                {
                    _context.Vtex.Logger.Warn("ParseLong", null, $"Could not parse {value}");
                    return null;
                }
            }
        }

        private async Task<bool> ParseBool(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            else
            {
                bool retVal;
                if (bool.TryParse(value, out retVal))
                {
                    return retVal;
                }
                else
                {
                    _context.Vtex.Logger.Warn("ParseBool", null, $"Could not parse {value}");
                    return false;
                }
            }
        }

        private async Task<string> ToPrice(long? inPennies)
        {
            string priceString = string.Empty;
            if(inPennies != null)
            {
                decimal inDollars = (decimal)inPennies / 100;
                priceString = inDollars.ToString();
            }

            return priceString;
        }

        private async Task<string> GetColumnLetter(int columnNumber)
        {
            string columnLetter = string.Empty;
            int letterCode = columnNumber + 65;
            if (letterCode <= 90)
            {
                columnLetter = ((char)letterCode).ToString();
            }
            else
            {
                letterCode = letterCode - 26;
                columnLetter = ((char)letterCode).ToString();
                columnLetter = $"A{columnLetter}";
            }

            return columnLetter;
        }

        private async Task<ProductRequestV2> MergeProductRequestV2(ProductRequestV2 existingProduct, ProductRequestV2 newProduct, bool update = false)
        {
            Console.WriteLine("MergeProductRequestV2");
            foreach (Skus skus in newProduct.Skus)
            {
                Console.WriteLine($" - Adding sku '{skus.Id}'");
                if (existingProduct.Skus.Any(s => s.Id.Equals(skus.Id)))
                {
                    if (update)
                    {
                        existingProduct.Skus.Remove(existingProduct.Skus.FirstOrDefault(s => s.Id.Equals(skus.Id)));
                        existingProduct.Skus.Add(skus);
                    }
                    else
                    {
                        Console.WriteLine("Skipping...");
                    }
                }
                else
                {
                    existingProduct.Skus.Add(skus);
                }
            }

            return existingProduct;
        }
    }
}
