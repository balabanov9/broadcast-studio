namespace Broadcast.Api.Infrastructure;
public sealed record StoragePreset(string Id, string Name, string Endpoint, string Region, bool PathStyle, string Documentation);
public static class StoragePresets
{
    public static readonly StoragePreset[] All =
    [
        new("yandex", "Яндекс Object Storage", "https://storage.yandexcloud.net", "ru-central1", true, "https://yandex.cloud/ru/docs/storage/s3/s3-api-quickstart"),
        new("selectel-ru1", "Selectel · Санкт-Петербург ru-1", "https://s3.ru-1.storage.selcloud.ru", "ru-1", false, "https://docs.selectel.ru/s3/manage/configure-storage-update/"),
        new("selectel-ru3", "Selectel · Санкт-Петербург ru-3", "https://s3.ru-3.storage.selcloud.ru", "ru-3", false, "https://docs.selectel.ru/s3/manage/configure-storage-update/"),
        new("selectel-ru7", "Selectel · Москва ru-7", "https://s3.ru-7.storage.selcloud.ru", "ru-7", false, "https://docs.selectel.ru/s3/manage/configure-storage-update/"),
        new("vk", "VK Cloud · Москва", "https://hb.ru-msk.vkcloud-storage.ru", "ru-msk", true, "https://github.com/vk-cs/docs-public/tree/master/docs/ru/storage/s3")
    ];
}
