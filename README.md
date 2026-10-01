# OpIlGen

WPF-приложение (.NET 10) для превращения изображений в оптические иллюзии.

- MVVM: CommunityToolkit.Mvvm
- DI: Microsoft.Extensions.DependencyInjection (главное окно создаётся через контейнер)
- Результат сохраняется в папку `Output` рядом с exe

## Преобразователи
- Чёрно-белое (строго 2 цвета)

Новый преобразователь: реализовать `IImageTransformer` и зарегистрировать в `App.ConfigureServices`.
