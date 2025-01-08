using System.Data.SqlClient;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Dapper;

namespace StoreInfo.Function
{
    public class Everyflow
    {
        private readonly ILogger<Everyflow> _logger;
        private static readonly HttpClient httpClient = new HttpClient();

        public Everyflow(ILogger<Everyflow> logger)
        {
            _logger = logger;
        }

        [Function("Everyflow")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            _logger.LogInformation("Starting function execution.");

            string connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
            string apiKey = "z0GUjndQca0wNUl4ju6w";
            string apiUrl = "https://api.eflow.team/v1/networks/reporting/conversions?page={0}&page_size=2000&order_field=&order_direction=";

            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("Database connection string is not set.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            int currentPage = 1;
            bool morePages = true;

            try
            {
                while (morePages)
                {
                    _logger.LogInformation($"Fetching page {currentPage}.");

                    var requestBody = new
                    {
                        timezone_id = 90,
                        currency_id = "USD",
                        from = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd"),
                        to = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"),
                        show_events = true,
                        show_conversions = true,
                        query = new
                        {
                            filters = new[] { new { resource_type = "status", filter_id_value = "approved" } },
                            search_terms = new string[] { }
                        }
                    };

                    _logger.LogInformation($"requestbody: {requestBody}.");

                    string jsonRequestBody = JsonSerializer.Serialize(requestBody);
                    var httpContent = new StringContent(jsonRequestBody, Encoding.UTF8, "application/json");
                    httpContent.Headers.Add("X-Eflow-API-Key", apiKey);

                    var formattedUrl = string.Format(apiUrl, currentPage);
                    var response = await httpClient.PostAsync(formattedUrl, httpContent);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError($"API request failed with status code: {response.StatusCode}. Reason: {response.ReasonPhrase}");
                        return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                    }

                    string responseBody = await response.Content.ReadAsStringAsync();
                    var apiResponse = JsonSerializer.Deserialize<ApiResponse>(responseBody);

                    _logger.LogInformation($"Raw API Response: {responseBody}");

                    if (apiResponse == null || apiResponse.Conversions == null || !apiResponse.Conversions.Any())
                    {
                        _logger.LogWarning("API response data is empty.");
                        return new OkObjectResult("No data to process.");
                    }

                    using (var connection = new SqlConnection(connectionString))
                    {
                        connection.Open();

                        foreach (var order in apiResponse.Conversions)
                        {
                            if (order == null)
                            {
                                _logger.LogWarning("Order is null. Skipping.");
                                continue;
                            }

                            if (!Guid.TryParse(order.ConversionId, out Guid conversionId))
                            {
                                _logger.LogWarning($"Invalid GUID for ConversionId: {order.ConversionId}. Skipping.");
                                continue;
                            }

                            // Use the parsed GUID (conversionId) for insertion
                            string insertOrderQuery = @"
    INSERT INTO AffiliateOrders (
        conversion_id, conversion_unix_timestamp, sub1, sub2, sub3, sub4, sub5,
        source_id, status, payout_type, revenue_type, payout, revenue, session_user_ip,
        conversion_user_ip, country, region, city, dma, carrier, platform, os_version,
        device_type, device_model, brand, browser, language, http_user_agent, adv1, adv2,
        adv3, adv4, adv5, is_event, event, notes, transaction_id, click_unix_timestamp,
        error_code, error_message, sale_amount, is_scrub, coupon_code, order_id, url, isp,
        referer, app_id, currency_id, email, is_view_through, previous_network_offer_id,
        relationship_offer_network_offer_id, relationship_offer_network_id, 
        relationship_offer_name, relationship_offer_offer_status,
        relationship_advertiser_network_advertiser_id, relationship_advertiser_network_id,
        relationship_advertiser_name, relationship_advertiser_account_status,
        relationship_account_manager_network_employee_id,
        relationship_account_manager_network_id, relationship_account_manager_first_name,
        relationship_account_manager_last_name, relationship_account_manager_full_name,
        relationship_affiliate_network_affiliate_id, relationship_affiliate_network_id,
        relationship_affiliate_name, relationship_affiliate_account_status,
        relationship_affiliate_manager_network_employee_id,
        relationship_affiliate_manager_network_id, relationship_affiliate_manager_first_name,
        relationship_affiliate_manager_last_name, relationship_affiliate_manager_full_name,
        relationship_affiliate_manager_account_status, relationship_query_parameters,
        relationship_attribution_method, relationship_usm_data, network_offer_payout_revenue_id
    )
    VALUES (
        @ConversionId, @ConversionUnixTimestamp, @Sub1, @Sub2, @Sub3, @Sub4, @Sub5,
        @SourceId, @Status, @PayoutType, @RevenueType, @Payout, @Revenue, @SessionUserIp,
        @ConversionUserIp, @Country, @Region, @City, @Dma, @Carrier, @Platform, @OsVersion,
        @DeviceType, @DeviceModel, @Brand, @Browser, @Language, @HttpUserAgent, @Adv1, @Adv2,
        @Adv3, @Adv4, @Adv5, @IsEvent, @Event, @Notes, @TransactionId, @ClickUnixTimestamp,
        @ErrorCode, @ErrorMessage, @SaleAmount, @IsScrub, @CouponCode, @OrderId, @Url, @Isp,
        @Referer, @AppId, @CurrencyId, @Email, @IsViewThrough, @PreviousNetworkOfferId,
        @RelationshipOfferNetworkOfferId, @RelationshipOfferNetworkId, 
        @RelationshipOfferName, @RelationshipOfferOfferStatus,
        @RelationshipAdvertiserNetworkAdvertiserId, @RelationshipAdvertiserNetworkId,
        @RelationshipAdvertiserName, @RelationshipAdvertiserAccountStatus,
        @RelationshipAccountManagerNetworkEmployeeId,
        @RelationshipAccountManagerNetworkId, @RelationshipAccountManagerFirstName,
        @RelationshipAccountManagerLastName, @RelationshipAccountManagerFullName,
        @RelationshipAffiliateNetworkAffiliateId, @RelationshipAffiliateNetworkId,
        @RelationshipAffiliateName, @RelationshipAffiliateAccountStatus,
        @RelationshipAffiliateManagerNetworkEmployeeId,
        @RelationshipAffiliateManagerNetworkId, @RelationshipAffiliateManagerFirstName,
        @RelationshipAffiliateManagerLastName, @RelationshipAffiliateManagerFullName,
        @RelationshipAffiliateManagerAccountStatus, @RelationshipQueryParameters,
        @RelationshipAttributionMethod, @RelationshipUsmData, @NetworkOfferPayoutRevenueId
    );";

                            await connection.ExecuteAsync(insertOrderQuery, new
                            {
                                ConversionId = conversionId,
                                order.ConversionUnixTimestamp,
                                order.Sub1,
                                order.Sub2,
                                order.Sub3,
                                order.Sub4,
                                order.Sub5,
                                order.SourceId,
                                order.Status,
                                order.PayoutType,
                                order.RevenueType,
                                order.Payout,
                                order.Revenue,
                                order.SessionUserIp,
                                order.ConversionUserIp,
                                order.Country,
                                order.Region,
                                order.City,
                                order.Dma,
                                order.Carrier,
                                order.Platform,
                                order.OsVersion,
                                order.DeviceType,
                                order.DeviceModel,
                                order.Brand,
                                order.Browser,
                                order.Language,
                                order.HttpUserAgent,
                                order.Adv1,
                                order.Adv2,
                                order.Adv3,
                                order.Adv4,
                                order.Adv5,
                                order.IsEvent,
                                order.Event,
                                order.Notes,
                                order.TransactionId,
                                order.ClickUnixTimestamp,
                                order.ErrorCode,
                                order.ErrorMessage,
                                order.SaleAmount,
                                order.IsScrub,
                                order.CouponCode,
                                order.OrderId,
                                order.Url,
                                order.Isp,
                                order.Referer,
                                order.AppId,
                                order.CurrencyId,
                                order.Email,
                                order.IsViewThrough,
                                order.PreviousNetworkOfferId,
                                RelationshipOfferNetworkOfferId = order.Relationship.Offer.NetworkOfferId,
                                RelationshipOfferNetworkId = order.Relationship.Offer.NetworkId,
                                RelationshipOfferName = order.Relationship.Offer.Name,
                                RelationshipOfferOfferStatus = order.Relationship.Offer.OfferStatus,
                                RelationshipAdvertiserNetworkAdvertiserId = order.Relationship.Advertiser.NetworkAdvertiserId,
                                RelationshipAdvertiserNetworkId = order.Relationship.Advertiser.NetworkId,
                                RelationshipAdvertiserName = order.Relationship.Advertiser.Name,
                                RelationshipAdvertiserAccountStatus = order.Relationship.Advertiser.AccountStatus,
                                RelationshipAccountManagerNetworkEmployeeId = order.Relationship.AccountManager.NetworkEmployeeId,
                                RelationshipAccountManagerNetworkId = order.Relationship.AccountManager.NetworkId,
                                RelationshipAccountManagerFirstName = order.Relationship.AccountManager.FirstName,
                                RelationshipAccountManagerLastName = order.Relationship.AccountManager.LastName,
                                RelationshipAccountManagerFullName = order.Relationship.AccountManager.FullName,
                                RelationshipAffiliateNetworkAffiliateId = order.Relationship.Affiliate.NetworkAffiliateId,
                                RelationshipAffiliateNetworkId = order.Relationship.Affiliate.NetworkId,
                                RelationshipAffiliateName = order.Relationship.Affiliate.Name,
                                RelationshipAffiliateAccountStatus = order.Relationship.Affiliate.AccountStatus,
                                RelationshipAffiliateManagerNetworkEmployeeId = order.Relationship.AffiliateManager.NetworkEmployeeId,
                                RelationshipAffiliateManagerNetworkId = order.Relationship.AffiliateManager.NetworkId,
                                RelationshipAffiliateManagerFirstName = order.Relationship.AffiliateManager.FirstName,
                                RelationshipAffiliateManagerLastName = order.Relationship.AffiliateManager.LastName,
                                RelationshipAffiliateManagerFullName = order.Relationship.AffiliateManager.FullName,
                                RelationshipAffiliateManagerAccountStatus = order.Relationship.AffiliateManager.AccountStatus,
                                RelationshipQueryParameters = JsonSerializer.Serialize(order.Relationship.QueryParameters),
                                RelationshipAttributionMethod = order.Relationship.AttributionMethod,
                                RelationshipUsmData = order.Relationship.UsmData,
                                NetworkOfferPayoutRevenueId = order.NetworkOfferPayoutRevenueId
                            });

                            if (!Guid.TryParse(order.ConversionId, out Guid parsedConversionId))
                            {
                                _logger.LogWarning($"Invalid GUID for ConversionId: {order.ConversionId}. Skipping line item.");
                                continue;
                            }
                            
                        
                            if (order.Relationship?.OrderLineItems != null && order.Relationship.OrderLineItems.Any())
                            {
                                foreach (var item in order.Relationship.OrderLineItems)
                                {
                                    _logger.LogInformation($"Processing line item for Order {order.ConversionId}: {JsonSerializer.Serialize(item)}");

                                    try
                                    {
                                        await connection.ExecuteAsync(@"
                INSERT INTO AffiliateOrderItems (
                    conversion_id, network_order_line_item_id, order_id, order_number,
                    product_id, sku, name, quantity, price, discount
                )
                VALUES (
                    @ConversionId, @NetworkOrderLineItemId, @OrderId, @OrderNumber,
                    @ProductId, @Sku, @Name, @Quantity, @Price, @Discount
                );",
                                            new
                                            {
                                                ConversionId = parsedConversionId,
                                                item.NetworkOrderLineItemId,
                                                item.OrderId,
                                                item.OrderNumber,
                                                item.ProductId,
                                                item.Sku,
                                                item.Name,
                                                item.Quantity,
                                                item.Price,
                                                item.Discount
                                            });
                                    }
                                    catch (SqlException ex)
                                    {
                                        _logger.LogError($"SQL error inserting line item for Order {order.ConversionId}: {ex.Message}");
                                    }
                                }
                            }
                            else
                            {
                                _logger.LogWarning($"Order {order.ConversionId} has no line items.");
                            }

                        }
                    }

                    morePages = apiResponse.Paging.Page * apiResponse.Paging.PageSize < apiResponse.Paging.TotalCount;
                    currentPage++;
                }

                _logger.LogInformation("Function executed successfully.");
                return new OkObjectResult("Data successfully ingested.");
            }
            catch (SqlException sqlEx)
            {
                _logger.LogError($"SQL error: {sqlEx.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
            catch (Exception ex)
            {
                _logger.LogError($"An unexpected error occurred: {ex.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        public class ApiResponse
        {
            [JsonPropertyName("paging")]
            public Paging Paging { get; set; }

            [JsonPropertyName("conversions")]
            public Order[] Conversions { get; set; }
        }

        public class Paging
        {
            [JsonPropertyName("page")]
            public int Page { get; set; }

            [JsonPropertyName("page_size")]
            public int PageSize { get; set; }

            [JsonPropertyName("total_count")]
            public int TotalCount { get; set; }
        }

        public class Order
        {
            [JsonPropertyName("conversion_id")]
            public string ConversionId { get; set; }

            [JsonPropertyName("conversion_unix_timestamp")]
            public long ConversionUnixTimestamp { get; set; }

            [JsonPropertyName("sub1")]
            public string Sub1 { get; set; }

            [JsonPropertyName("sub2")]
            public string Sub2 { get; set; }

            [JsonPropertyName("sub3")]
            public string Sub3 { get; set; }

            [JsonPropertyName("sub4")]
            public string Sub4 { get; set; }

            [JsonPropertyName("sub5")]
            public string Sub5 { get; set; }

            [JsonPropertyName("source_id")]
            public string SourceId { get; set; }

            [JsonPropertyName("status")]
            public string Status { get; set; }

            [JsonPropertyName("payout_type")]
            public string PayoutType { get; set; }

            [JsonPropertyName("revenue_type")]
            public string RevenueType { get; set; }

            [JsonPropertyName("payout")]
            public decimal Payout { get; set; }

            [JsonPropertyName("revenue")]
            public decimal Revenue { get; set; }

            [JsonPropertyName("session_user_ip")]
            public string SessionUserIp { get; set; }

            [JsonPropertyName("conversion_user_ip")]
            public string ConversionUserIp { get; set; }

            [JsonPropertyName("country")]
            public string Country { get; set; }

            [JsonPropertyName("region")]
            public string Region { get; set; }

            [JsonPropertyName("city")]
            public string City { get; set; }

            [JsonPropertyName("dma")]
            public int? Dma { get; set; }

            [JsonPropertyName("carrier")]
            public string Carrier { get; set; }

            [JsonPropertyName("platform")]
            public string Platform { get; set; }

            [JsonPropertyName("os_version")]
            public string OsVersion { get; set; }

            [JsonPropertyName("device_type")]
            public string DeviceType { get; set; }

            [JsonPropertyName("device_model")]
            public string DeviceModel { get; set; }

            [JsonPropertyName("brand")]
            public string Brand { get; set; }

            [JsonPropertyName("browser")]
            public string Browser { get; set; }

            [JsonPropertyName("language")]
            public string Language { get; set; }

            [JsonPropertyName("http_user_agent")]
            public string HttpUserAgent { get; set; }

            [JsonPropertyName("adv1")]
            public string Adv1 { get; set; }

            [JsonPropertyName("adv2")]
            public string Adv2 { get; set; }

            [JsonPropertyName("adv3")]
            public string Adv3 { get; set; }

            [JsonPropertyName("adv4")]
            public string Adv4 { get; set; }

            [JsonPropertyName("adv5")]
            public string Adv5 { get; set; }

            [JsonPropertyName("is_event")]
            public bool? IsEvent { get; set; }

            [JsonPropertyName("event")]
            public string Event { get; set; }

            [JsonPropertyName("notes")]
            public string Notes { get; set; }

            [JsonPropertyName("transaction_id")]
            public string TransactionId { get; set; }

            [JsonPropertyName("click_unix_timestamp")]
            public long? ClickUnixTimestamp { get; set; }

            [JsonPropertyName("error_code")]
            public int? ErrorCode { get; set; }

            [JsonPropertyName("error_message")]
            public string ErrorMessage { get; set; }

            [JsonPropertyName("sale_amount")]
            public decimal? SaleAmount { get; set; }

            [JsonPropertyName("is_scrub")]
            public bool? IsScrub { get; set; }

            [JsonPropertyName("coupon_code")]
            public string CouponCode { get; set; }

            [JsonPropertyName("order_id")]
            public string OrderId { get; set; }

            [JsonPropertyName("url")]
            public string Url { get; set; }

            [JsonPropertyName("isp")]
            public string Isp { get; set; }

            [JsonPropertyName("referer")]
            public string Referer { get; set; }

            [JsonPropertyName("app_id")]
            public string AppId { get; set; }

            [JsonPropertyName("currency_id")]
            public string CurrencyId { get; set; }

            [JsonPropertyName("email")]
            public string Email { get; set; }

            [JsonPropertyName("is_view_through")]
            public bool? IsViewThrough { get; set; }

            [JsonPropertyName("previous_network_offer_id")]
            public int? PreviousNetworkOfferId { get; set; }

            [JsonPropertyName("relationship")]
            public Relationship Relationship { get; set; }

            [JsonPropertyName("relationship_offer_network_offer_id")]
            public int? RelationshipOfferNetworkOfferId { get; set; }

            [JsonPropertyName("relationship_offer_network_id")]
            public int? RelationshipOfferNetworkId { get; set; }

            [JsonPropertyName("relationship_offer_name")]
            public string RelationshipOfferName { get; set; }

            [JsonPropertyName("relationship_offer_offer_status")]
            public string RelationshipOfferOfferStatus { get; set; }

            [JsonPropertyName("relationship_advertiser_network_advertiser_id")]
            public int? RelationshipAdvertiserNetworkAdvertiserId { get; set; }

            [JsonPropertyName("relationship_advertiser_network_id")]
            public int? RelationshipAdvertiserNetworkId { get; set; }

            [JsonPropertyName("relationship_advertiser_name")]
            public string RelationshipAdvertiserName { get; set; }

            [JsonPropertyName("relationship_advertiser_account_status")]
            public string RelationshipAdvertiserAccountStatus { get; set; }

            [JsonPropertyName("relationship_account_manager_network_employee_id")]
            public int? RelationshipAccountManagerNetworkEmployeeId { get; set; }

            [JsonPropertyName("relationship_account_manager_network_id")]
            public int? RelationshipAccountManagerNetworkId { get; set; }

            [JsonPropertyName("relationship_account_manager_first_name")]
            public string RelationshipAccountManagerFirstName { get; set; }

            [JsonPropertyName("relationship_account_manager_last_name")]
            public string RelationshipAccountManagerLastName { get; set; }

            [JsonPropertyName("relationship_account_manager_full_name")]
            public string RelationshipAccountManagerFullName { get; set; }

            [JsonPropertyName("relationship_affiliate_network_affiliate_id")]
            public int? RelationshipAffiliateNetworkAffiliateId { get; set; }

            [JsonPropertyName("relationship_affiliate_network_id")]
            public int? RelationshipAffiliateNetworkId { get; set; }

            [JsonPropertyName("relationship_affiliate_name")]
            public string RelationshipAffiliateName { get; set; }

            [JsonPropertyName("relationship_affiliate_account_status")]
            public string RelationshipAffiliateAccountStatus { get; set; }

            [JsonPropertyName("relationship_affiliate_manager_network_employee_id")]
            public int? RelationshipAffiliateManagerNetworkEmployeeId { get; set; }

            [JsonPropertyName("relationship_affiliate_manager_network_id")]
            public int? RelationshipAffiliateManagerNetworkId { get; set; }

            [JsonPropertyName("relationship_affiliate_manager_first_name")]
            public string RelationshipAffiliateManagerFirstName { get; set; }

            [JsonPropertyName("relationship_affiliate_manager_last_name")]
            public string RelationshipAffiliateManagerLastName { get; set; }

            [JsonPropertyName("relationship_affiliate_manager_full_name")]
            public string RelationshipAffiliateManagerFullName { get; set; }

            [JsonPropertyName("relationship_affiliate_manager_account_status")]
            public string RelationshipAffiliateManagerAccountStatus { get; set; }

            [JsonPropertyName("relationship_query_parameters")]
            public string RelationshipQueryParameters { get; set; }

            [JsonPropertyName("relationship_attribution_method")]
            public string RelationshipAttributionMethod { get; set; }

            [JsonPropertyName("relationship_usm_data")]
            public string RelationshipUsmData { get; set; }

            [JsonPropertyName("network_offer_payout_revenue_id")]
            public int? NetworkOfferPayoutRevenueId { get; set; }

            [JsonPropertyName("order_line_items")]
            public Item[] OrderLineItems { get; set; }
        }

        public class Relationship
        {
            [JsonPropertyName("offer")]
            public Offer Offer { get; set; }

            [JsonPropertyName("advertiser")]
            public Advertiser Advertiser { get; set; }

            [JsonPropertyName("account_manager")]
            public AccountManager AccountManager { get; set; }

            [JsonPropertyName("affiliate")]
            public Affiliate Affiliate { get; set; }

            [JsonPropertyName("affiliate_manager")]
            public AffiliateManager AffiliateManager { get; set; }

            [JsonPropertyName("query_parameters")]
            public Dictionary<string, string> QueryParameters { get; set; }

            [JsonPropertyName("attribution_method")]
            public string AttributionMethod { get; set; }

            [JsonPropertyName("usm_data")]
            public string UsmData { get; set; }

            [JsonPropertyName("order_line_items")]
            public List<Item> OrderLineItems { get; set; }
        }

        public class Offer
        {
            [JsonPropertyName("network_offer_id")]
            public int NetworkOfferId { get; set; }

            [JsonPropertyName("network_id")]
            public int NetworkId { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("offer_status")]
            public string OfferStatus { get; set; }
        }

        public class Advertiser
        {
            [JsonPropertyName("network_advertiser_id")]
            public int NetworkAdvertiserId { get; set; }

            [JsonPropertyName("network_id")]
            public int NetworkId { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("account_status")]
            public string AccountStatus { get; set; }
        }

        public class AccountManager
        {
            [JsonPropertyName("network_employee_id")]
            public int NetworkEmployeeId { get; set; }

            [JsonPropertyName("network_id")]
            public int NetworkId { get; set; }

            [JsonPropertyName("first_name")]
            public string FirstName { get; set; }

            [JsonPropertyName("last_name")]
            public string LastName { get; set; }

            [JsonPropertyName("full_name")]
            public string FullName { get; set; }

            [JsonPropertyName("account_status")]
            public string AccountStatus { get; set; }
        }

        public class Affiliate
        {
            [JsonPropertyName("network_affiliate_id")]
            public int NetworkAffiliateId { get; set; }

            [JsonPropertyName("network_id")]
            public int NetworkId { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("account_status")]
            public string AccountStatus { get; set; }
        }

        public class AffiliateManager
        {
            [JsonPropertyName("network_employee_id")]
            public int NetworkEmployeeId { get; set; }

            [JsonPropertyName("network_id")]
            public int NetworkId { get; set; }

            [JsonPropertyName("first_name")]
            public string FirstName { get; set; }

            [JsonPropertyName("last_name")]
            public string LastName { get; set; }

            [JsonPropertyName("full_name")]
            public string FullName { get; set; }

            [JsonPropertyName("account_status")]
            public string AccountStatus { get; set; }
        }


        public class Item
        {
            [JsonPropertyName("network_order_line_item_id")]
            public int NetworkOrderLineItemId { get; set; }

            [JsonPropertyName("order_id")]
            public int OrderId { get; set; }

            [JsonPropertyName("order_number")]
            public int OrderNumber { get; set; }

            [JsonPropertyName("product_id")]
            public long ProductId { get; set; }

            [JsonPropertyName("sku")]
            public string Sku { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("quantity")]
            public int Quantity { get; set; }

            [JsonPropertyName("price")]
            public decimal Price { get; set; }

            [JsonPropertyName("discount")]
            public decimal Discount { get; set; }
        }

    }
}