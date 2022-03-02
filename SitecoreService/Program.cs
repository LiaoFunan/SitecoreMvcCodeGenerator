using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace SitecoreService.CodeGenerator
{
    internal class Program
    {
        public static readonly string ACCOUNT = ConfigurationManager.AppSettings["Account"];
        public static readonly string LOCAL_SITECORE_URL = ConfigurationManager.AppSettings["LocalSiteCoreUrl"];
        public static readonly string PASSWORD = ConfigurationManager.AppSettings["Password"];
        public static void Main(string[] args)
        {
            try
            {
                CookieContainer cookies = GetCookies();
                List<string> itemIDs = ConfigurationManager.AppSettings.AllKeys
                             .Where(key => key.StartsWith("ItemGuid"))
                             .Select(key => ConfigurationManager.AppSettings[key])
                             .ToList();
                foreach (var id in itemIDs)
                {
                    GenerateFile(cookies, id);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred. Message: {ex.Message}.\r\n StackTrace: {ex.StackTrace}.\r\n InnerException: {ex.InnerException}");
            }

            Console.ReadKey();
        }

        private static void GenerateFile(CookieContainer cookies, string itemId)
        {
            Item item = GetItem(cookies, itemId);

            Item templateItem = GetItem(cookies, item.TemplateID);
            List<Item> items = GetPopertItems(cookies, templateItem);
            var className = templateItem.ItemName.Replace(" ", "");

            List<Item> childItems = GetChildItems(cookies, itemId);
            Item childItem = childItems.FirstOrDefault();
            string childTemplateDisplayName = string.Empty;
            if (childItem != null)
            {
                Item childTemplateItem = GetItem(cookies, childItem.TemplateID);
                childTemplateDisplayName = childTemplateItem.DisplayName;
            }

            ModelTemplate template = new ModelTemplate
            {
                Items = items,
                ClassName = className,
                DisplayName = templateItem.DisplayName,
                ChildID = childItem?.TemplateID,
                ChildClassName = childItem?.TemplateName,
                ChildDisplayName = childTemplateDisplayName
            };

            string content = template.TransformText();
            File.WriteAllText(className + "Model.cs", content);

            string childFields = string.Empty;

            if (childItem != null)
            {

                Item childTemplateItem = GetItem(cookies, childItem.TemplateID);
                List<Item> childTemplateItems = GetPopertItems(cookies, childTemplateItem);
                ItemTemplate childTemplate = new ItemTemplate
                {
                    Items = childTemplateItems,
                    ClassName = childTemplateItem.ItemName.Replace(" ", ""),
                    DisplayName = childTemplateItem.DisplayName,
                };

                string childContent = childTemplate.TransformText();
                File.WriteAllText(childItem.TemplateName.Replace(" ", "") + "Item.cs", childContent);

                childFields = GetFields(childTemplateItems, childTemplate.ClassName);

                childFields = childFields.Replace("Model.DatasourceItem","item");
            }

            string fields = GetFields(items, className);


            BuildController(className);

            BuildCshtml(className, fields, childFields, childItem?.TemplateName);
        }

        private static string GetFields(List<Item> items, string className)
        {
            string fields;
            StringBuilder sb = new StringBuilder();

            foreach (Item field in items)
            {
                if (field.Type == "General Link")
                {
                    sb.Append($"@Html.Sitecore().Field(E.SunBank.Project.eFingo.Models.{className}.{className}Template.FieldIDList.{field.ItemName}.ToString(), Model.DatasourceItem.DataItem)");
                    sb.Append(Environment.NewLine);
                }
                else if (field.Type == "Multi-Line Text" || field.Type == "Rich Text")
                {
                    sb.Append($"@Html.Raw(Model.DatasourceItem.{field.ItemName})");
                    sb.Append(Environment.NewLine);
                }
                else
                {
                    sb.Append($"@Model.DatasourceItem.{field.ItemName}");
                    sb.Append(Environment.NewLine);
                }
            }

            fields = sb.ToString();
            return fields;
        }

        private static List<Item> GetPopertItems(CookieContainer cookies, Item templateItem)
        {
            List<Item> templateMiddleitems = GetChildItems(cookies, templateItem.ItemID);
            var templateMiddleId = templateMiddleitems.FirstOrDefault().ItemID;
            List<Item> items = GetChildItems(cookies, templateMiddleId);
            return items;
        }

        private static void BuildCshtml(string className, string fields, string childFields, string childClass)
        {
            string childBlock = string.Empty;
            if (!string.IsNullOrEmpty(childClass))
            {
                childBlock = $@"@foreach(var item in Model.DatasourceItem.{childClass}Items)
                {{
                    {childFields}
                }}";
            }


            string cshtml = $@"@model E.SunBank.Project.eFingo.Models.{className}.{className}Model
            @if (Model != null && Model.DatasourceItem != null)
            {{
                {fields}

                {childBlock}
            }}";
            System.IO.FileInfo file = new System.IO.FileInfo(className + "/" + className);
            file.Directory.Create();
            File.WriteAllText(className + "/" + className + ".cshtml", cshtml);
        }

        private static void BuildController(string className)
        {
            string controller = $@"using E.SunBank.Project.eFingo.Models.{className};
using Sitecore.Data.Items;
using Sitecore.Mvc.Presentation;
using System.Web.Mvc;

namespace E.SunBank.Project.eFingo.Controllers
{{
    public class {className}Controller : BaseController
    {{
        public ActionResult {className}()
        {{
            string dataSourceId = RenderingContext.CurrentOrNull.Rendering.DataSource;
            if (string.IsNullOrEmpty(dataSourceId))
            {{
                return null;
            }}
            Item dataSourceItem = Sitecore.Context.Database.GetItem(dataSourceId);
            {className}Item datasource = new {className}Item(dataSourceItem);
            {className}Model model = new {className}Model(datasource);
            return PartialView(this.ViewPath, model);
         }}
     }}
 }}";
            File.WriteAllText(className + "Controller.cs", controller);
        }

        private static Item GetItem(CookieContainer cookies, string guid)
        {
            var url = $"https://{LOCAL_SITECORE_URL}/sitecore/api/ssc/item/" + guid;

            var request = (HttpWebRequest)WebRequest.Create(url);

            request.Method = "GET";
            request.ContentType = "application/json";
            request.CookieContainer = cookies;

            var response = request.GetResponse();


            string responseString = string.Empty;
            using (Stream stream = response.GetResponseStream())
            {
                StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                responseString = reader.ReadToEnd();
            }

            var item = JsonConvert.DeserializeObject<Item>(responseString);
            return item;
        }

        private static List<Item> GetChildItems(CookieContainer cookies, string guid)
        {
            var url = $"https://{LOCAL_SITECORE_URL}/sitecore/api/ssc/item/" + guid + "/children";

            var request = (HttpWebRequest)WebRequest.Create(url);

            request.Method = "GET";
            request.ContentType = "application/json";
            request.CookieContainer = cookies;

            var response = request.GetResponse();


            string responseString = string.Empty;
            using (Stream stream = response.GetResponseStream())
            {
                StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                responseString = reader.ReadToEnd();
            }


            var items = JsonConvert.DeserializeObject<List<Item>>(responseString).Select(i => new Item
            {
                ItemID = i.ItemID,
                ItemName = i.ItemName.Replace(" ", ""),
                DisplayName = i.DisplayName,
                Type = i.Type,
                TemplateID = i.TemplateID,
                TemplateName = i.TemplateName,
                TemplateDisplayName = i.TemplateDisplayName,
                Source = i.Source,  
                HasChildren = i.HasChildren,
            }).ToList();
            return items;
        }

        private static CookieContainer GetCookies()
        {
            var authUrl = $"https://{LOCAL_SITECORE_URL}/sitecore/api/ssc/auth/login";
            var authData = new Authentication
            {
                Domain = "sitecore",
                Username = ACCOUNT,
                Password = PASSWORD,
            };

            var authRequest = (HttpWebRequest)WebRequest.Create(authUrl);

            authRequest.Method = "POST";
            authRequest.ContentType = "application/json";

            var requestAuthBody = JsonConvert.SerializeObject(authData);

            var authDatas = new UTF8Encoding().GetBytes(requestAuthBody);

            using (var dataStream = authRequest.GetRequestStream())
            {
                dataStream.Write(authDatas, 0, authDatas.Length);
            }

            CookieContainer cookies = new CookieContainer();

            authRequest.CookieContainer = cookies;


            ServicePointManager.ServerCertificateValidationCallback = (obj, certificate, chain, errors) => true;

            var authResponse = authRequest.GetResponse();

            Console.WriteLine($"Login Status:\n\r{((HttpWebResponse)authResponse).StatusDescription}");

            authResponse.Close();
            return cookies;
        }
    }



    public class Item
    {
        public string ItemName { get; set; }
        public string ItemID { get; set; }

        public string DisplayName { get; set; }

        public string Type { get; set; }

        public string TemplateID { get; set; }

        public string TemplateName { get; set; }

        public string TemplateDisplayName { get; set; }

        public string Source { get; set; }

        public bool HasChildren { get; set; }
    }





    public class Authentication
    {
        [JsonProperty("domain")]
        public string Domain { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("password")]
        public string Password { get; set; }
    }


    public class ItemRequest
    {
        public string ItemName { get; set; }

        public string TemplateID { get; set; }

        public string Title { get; set; }

        public string Text { get; set; }
    }
}
