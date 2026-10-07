// ==UserScript==
// @name         OpIlGen: открыть изображение в программе
// @namespace    https://github.com/dmsm-ven/OpIlGen
// @version      1.0.0
// @description  На странице с прямой ссылкой на изображение (.jpg/.png/...) показывает кнопку «Открыть в OpIlGen»
// @match        http://*/*
// @match        https://*/*
// @run-at       document-end
// @noframes
// @grant        none
// ==/UserScript==

(function () {
    'use strict';

    // ---- Настройки ----
    const LABEL = 'Открыть в OpIlGen';
    const BUTTON_OPACITY = 0.25;   // в покое; при наведении кнопка становится полностью непрозрачной
    const SCHEME = 'opilgen';
    const SUPPORTED_TYPES = /^image\/(jpeg|png|gif|bmp|tiff)/i; // форматы, которые умеет открывать WPF

    // Скрипт срабатывает только на «голых» страницах изображений (Chrome сам оборачивает их в документ с одним <img>)
    if (!SUPPORTED_TYPES.test(document.contentType || '')) {
        return;
    }

    const host = document.createElement('div');
    host.style.cssText = 'all: initial; position: fixed; top: 16px; right: 16px; z-index: 2147483647;';

    // Shadow DOM, чтобы стили страницы не влияли на кнопку (и наоборот)
    const root = host.attachShadow({ mode: 'closed' });
    root.innerHTML = `
        <style>
            button {
                font: 600 14px/1 "Segoe UI", system-ui, sans-serif;
                color: #fff;
                background: #7c4dff;
                border: 0;
                border-radius: 8px;
                padding: 10px 16px;
                cursor: pointer;
                opacity: ${BUTTON_OPACITY};
                box-shadow: 0 2px 8px rgba(0, 0, 0, .45);
                transition: opacity .15s ease, transform .15s ease;
            }
            button:hover { opacity: 1; transform: translateY(-1px); }
            button:active { transform: none; }
        </style>
        <button type="button"></button>`;

    const button = root.querySelector('button');
    button.textContent = LABEL;
    button.addEventListener('click', () => {
        // Адрес кодируется целиком: query-параметры и & в ссылке на картинку не ломают схему
        window.location.assign(`${SCHEME}://open?url=${encodeURIComponent(location.href)}`);
    });

    (document.body || document.documentElement).appendChild(host);
})();
