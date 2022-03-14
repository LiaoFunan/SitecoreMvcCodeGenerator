using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SitecoreService.CodeGenerator;

namespace SitecoreService.MvcCodeGenerator
{
    internal class Program
    {
        public static readonly string ACCOUNT = ConfigurationManager.AppSettings["Account"];
        public static readonly string LOCAL_SITECORE_URL = ConfigurationManager.AppSettings["LocalSiteCoreUrl"];
        public static readonly string PASSWORD = ConfigurationManager.AppSettings["Password"];
        public static readonly string NAMESPACE = ConfigurationManager.AppSettings["NameSpace"];
        public static readonly string LANGUAGE = ConfigurationManager.AppSettings["Language"];
        
        public static void Main(string[] args)
        {
            //try
            //{
                CookieContainer cookies = GetCookies();
                List<string> itemIDs = ConfigurationManager.AppSettings.AllKeys
                             .Where(key => key.StartsWith("ItemGuid"))
                             .Select(key => ConfigurationManager.AppSettings[key])
                             .ToList();
                foreach (var id in itemIDs)
                {
                    GenerateFile(cookies, id);
                }
            //}
            //catch (Exception ex)
            //{
            //    Console.WriteLine(ex.ToString());
            //}

            Console.ReadKey();
        }

        private static void GenerateFile(CookieContainer cookies, string itemId)
        {
            Item item = GetItemByID(cookies, itemId);

            Item templateItem = GetItemByID(cookies,item.TemplateID);
            List<Item> propertyItems = GetPropertyItems(cookies, templateItem);


            string childFields = string.Empty;

            List<string> childClass = new List<string>();
            
            foreach (var propItem in propertyItems)
            {
                //取得連結item資訊 LinkDisplayName LinkItemID LinkItemName
                if (!string.IsNullOrEmpty(propItem.Source))
                {
                    var sourceItem = GetItemByPath(cookies, propItem.Source);
                    var optionItems = GetChildItems(cookies, (string)sourceItem.ItemID);

                    Item templatePropertyItem = GetItemByID(cookies, optionItems.FirstOrDefault().TemplateID);
                    List<Item> propItems = GetPropertyItems(cookies, templatePropertyItem);
                    var firstFieldItem = propItems.FirstOrDefault();
                    propItem.LinkDisplayName = firstFieldItem.DisplayName;
                    propItem.LinkItemID = firstFieldItem.ItemID;
                    propItem.LinkItemName = firstFieldItem.ItemName;
                }

                if(propItem.Type == "Treelist")
                {
                    var jObject = GetJObjectByID(cookies, itemId);
                    var jValue = (JValue)jObject[propItem.ItemName];
                    var childID = jValue.Value.ToString().Split('|')[0];
                    Item childObjectItem = GetItemByID(cookies, childID);
                    childFields += BuildChildItem(cookies, childObjectItem);
                    childClass.Add(propItem.ItemName);
                }
            }

            var className = templateItem.ItemName.Replace(" ", "");


            Item childItem = BuildModel(cookies, itemId, templateItem, propertyItems, className);

            if (childItem != null)
            {
                childFields += BuildChildItem(cookies, childItem);
            }

            string fields = GetFields(propertyItems, className);

            BuildController(className,item.DisplayName);

            BuildCshtml(className, fields, childFields, childClass);
        }

        private static string BuildChildItem(CookieContainer cookies, Item childItem)
        {
            string childFields;
            Item childTemplateItem = GetItemByID(cookies, childItem.TemplateID);
            List<Item> childTemplateItems = GetPropertyItems(cookies, childTemplateItem);
            ItemTemplate childTemplate = new ItemTemplate
            {
                PropertyItems = childTemplateItems,
                ClassName = childTemplateItem.ItemName.Replace(" ", ""),
                DisplayName = childTemplateItem.DisplayName,
            };

            string childContent = childTemplate.TransformText();
            File.WriteAllText(childItem.TemplateName.Replace(" ", "") + "Item.cs", childContent);

            childFields = GetFields(childTemplateItems, childTemplate.ClassName);

            childFields = childFields.Replace("Model.DatasourceItem", "item");
            return childFields;
        }

        private static Item BuildModel(CookieContainer cookies, string itemId, Item templateItem, List<Item> propertyItems, string className)
        {
            List<Item> childItems = GetChildItems(cookies, itemId);
            Item childItem = childItems.FirstOrDefault();
            string childTemplateDisplayName = string.Empty;
            if (childItem != null)
            {
                Item childTemplateItem = GetItemByID(cookies, childItem.TemplateID);
                childTemplateDisplayName = childTemplateItem.DisplayName;
            }

            ModelTemplate template = new ModelTemplate
            {
                PropertyItems = propertyItems,
                ClassName = className,
                DisplayName = templateItem.DisplayName,
                ChildID = childItem?.TemplateID,
                ChildClassName = childItem?.TemplateName.Replace(" ",""),
                ChildDisplayName = childTemplateDisplayName
            };

            string content = template.TransformText();
            File.WriteAllText(className + "Model.cs", content);
            return childItem;
        }

        private static string GetFields(List<Item> items, string className)
        {
            string fields;
            StringBuilder sb = new StringBuilder();

            foreach (Item field in items)
            {
                if (field.Type == "General Link")
                {
                    sb.Append($"@Html.Sitecore().Field({NAMESPACE}.Models.{className}.{className}Template.FieldIDList.{field.ItemName}.ToString(), Model.DatasourceItem.DataItem)");
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

        private static List<Item> GetPropertyItems(CookieContainer cookies, Item templateItem)
        {
            List<Item> templateMiddleitems = GetChildItems(cookies, templateItem.ItemID);
            var templateMiddleId = templateMiddleitems.FirstOrDefault().ItemID;
            List<Item> items = GetChildItems(cookies, templateMiddleId);
            return items;
        }

        private static void BuildCshtml(string className, string fields, string childFields, List<string> childClass)
        {
            string childBlock = string.Empty;
            if (childClass.Count() != 0)
            {
                childBlock = $@"@foreach(var item in Model.DatasourceItem.{childClass.FirstOrDefault()}Items)
                {{
                    {childFields}
                }}";
            }

            string cshtml = $@"@model {NAMESPACE}.Models.{className}.{className}Model
            @if (Model != null && Model.DatasourceItem != null)
            {{
                {fields}

                {childBlock}
            }}";
            FileInfo file = new FileInfo(className + "/" + className);
            file.Directory.Create();
            File.WriteAllText(className + "/" + className + ".cshtml", cshtml);
        }

        private static void BuildController(string className,string displayName)
        {
            string controller = $@"using {NAMESPACE}.Models.{className};
using Sitecore.Data.Items;
using Sitecore.Mvc.Presentation;
using System.Web.Mvc;

namespace {NAMESPACE}.Controllers
{{
    public class {className}Controller : BaseController
    {{
        /// <summary>
        /// {displayName}
        /// </summary>
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

        //private static dynamic GetItemByID(CookieContainer cookies, string guid)
        //{
        //    var url = $"https://{LOCAL_SITECORE_URL}/sitecore/api/ssc/item/{guid}";

        //    var request = (HttpWebRequest)WebRequest.Create(url);

        //    request.Method = "GET";
        //    request.ContentType = "application/json";
        //    request.CookieContainer = cookies;

        //    var response = request.GetResponse();


        //    string responseString = string.Empty;
        //    using (Stream stream = response.GetResponseStream())
        //    {
        //        StreamReader reader = new StreamReader(stream, Encoding.UTF8);
        //        responseString = reader.ReadToEnd();
        //    }

        //    var item = JObject.Parse(responseString);
        //    return item;
        //}


        private static Item GetItemByID(CookieContainer cookies, string guid)
        {
            var url = $"https://{LOCAL_SITECORE_URL}/sitecore/api/ssc/item/{guid}?language={LANGUAGE}";

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
            item.ItemName = item.ItemName.Replace(" ", "");
            return item;
        }


        private static JObject GetJObjectByID(CookieContainer cookies, string guid)
        {
            var url = $"https://{LOCAL_SITECORE_URL}/sitecore/api/ssc/item/{guid}?language={LANGUAGE}";

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


            JObject jObject = JsonConvert.DeserializeObject<JObject>(responseString);
            return jObject;
        }

        private static dynamic GetItemByPath(CookieContainer cookies, string path)
        {
            var url = $"https://{LOCAL_SITECORE_URL}/sitecore/api/ssc/item/?path={path}";

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

            var item = JObject.Parse(responseString);
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
        public string LinkItemName { get; set; }
        public string LinkItemID { get; set; }
        public string LinkDisplayName { get; set; }
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
