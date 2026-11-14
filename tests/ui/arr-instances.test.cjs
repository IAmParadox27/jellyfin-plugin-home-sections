const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { test, before, after } = require('node:test');
const { chromium } = require('playwright');

let browser;

before(async () =>
{
    const options = { headless: true };
    if (process.env.HSS_BROWSER_EXECUTABLE)
    {
        options.executablePath = process.env.HSS_BROWSER_EXECUTABLE;
    }
    browser = await chromium.launch(options);
});

after(async () =>
{
    if (browser)
    {
        await browser.close();
    }
});

async function openConfig(config)
{
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/*', route => route.fulfill({ status: 200, body: '' }));
    await page.goto('http://hss.test');
    const html = await fs.readFile(path.join(__dirname, '../../src/Jellyfin.Plugin.HomeScreenSections/Configuration/config.html'), 'utf8');
    const translations = JSON.parse((await fs.readFile(path.join(__dirname, '../../src/Jellyfin.Plugin.HomeScreenSections/_Localization/de.json'), 'utf8')).trim());
    await page.evaluate(({ config, translations }) =>
    {
        document.documentElement.lang = 'de';
        window.savedConfigs = [];
        window.Dashboard = {
            showLoadingMsg()
            {
            },
            hideLoadingMsg()
            {
            },
            processPluginConfigurationUpdateResult()
            {
            },
            alert(message)
            {
                throw new Error(message);
            }
        };
        window.ApiClient = {
            getUrl: route => 'http://hss.test/' + route,
            getVirtualFolders: async () => [],
            getPluginConfiguration: async () => config,
            updatePluginConfiguration: async (id, saved) =>
            {
                window.savedConfigs.push(saved);
            },
            ajax: async request => ({
                json: async () => request.url.includes('Translations') ? translations : { Items: [] }
            })
        };
    }, { config: { SectionSettings: [], Sonarr: {}, Radarr: {}, Lidarr: {}, Readarr: {}, ...config }, translations });
    await page.setContent(html);
    await page.evaluate(() =>
    {
        document.querySelector('#homeScreenSectionsConfigurationPage').dispatchEvent(new Event('pageshow'));
    });
    await page.waitForFunction(() => document.querySelector('[data-arr-service=Sonarr] span').textContent.includes('hinzufügen'));
    await page.locator('details').filter({ has: page.locator('#sonarrInstances') }).evaluate(element =>
    {
        element.open = true;
    });
    return { page, errors };
}

test('migrates legacy settings and saves two named instances per service', async () =>
{
    const { page, errors } = await openConfig({
        Sonarr: { Url: 'http://sonarr.test/base', ApiKey: 'old-sonarr' },
        Radarr: { Url: 'http://radarr.test', ApiKey: 'old-radarr' }
    });
    try
    {
        assert.equal(await page.locator('#sonarrInstances .arrInstance').count(), 1);
        assert.equal(await page.locator('#radarrInstances .arrInstance').count(), 1);
        for (const service of ['Sonarr', 'Radarr'])
        {
            await page.locator('[data-arr-service=' + service + ']').click();
            const rows = page.locator('#' + service.toLowerCase() + 'Instances .arrInstance');
            await rows.nth(0).locator('[data-field=Name]').fill(service + ' Standard');
            await rows.nth(1).locator('[data-field=Name]').fill('<img src=x onerror=alert(1)>');
            await rows.nth(1).locator('[data-field=Url]').fill('http://' + service.toLowerCase() + '-anime.test');
            await rows.nth(1).locator('[data-field=ApiKey]').fill('anime-key');
        }
        assert.equal(await page.locator('.arrInstance img').count(), 0);
        await page.locator('#btnSaveConfigGlobal').click();
        const saved = await page.evaluate(() => window.savedConfigs.at(-1));
        assert.equal(saved.Sonarr.Instances.length, 2);
        assert.equal(saved.Radarr.Instances.length, 2);
        assert.equal(saved.Sonarr.Instances[0].ApiKey, 'old-sonarr');
        assert.equal(saved.Radarr.Instances[1].Name, '<img src=x onerror=alert(1)>');
        assert.equal(saved.Sonarr.Url, '');
        assert.equal(saved.Radarr.ApiKey, '');
        assert.equal(saved.Lidarr.Url, '');
        assert.deepEqual(errors, []);
    }
    finally
    {
        await page.close();
    }
});

test('explicit empty arrays override legacy values and removal persists', async () =>
{
    const { page, errors } = await openConfig({
        Sonarr: { Url: 'http://legacy.test', ApiKey: 'old', Instances: [] },
        Radarr: { Instances: [{ Name: 'Anime', Url: 'http://anime.test', ApiKey: 'secret' }] }
    });
    try
    {
        assert.equal(await page.locator('#sonarrInstances .arrInstance').count(), 0);
        await page.locator('#radarrInstances [data-action=remove]').click();
        await page.locator('#btnSaveConfigGlobal').click();
        const saved = await page.evaluate(() => window.savedConfigs.at(-1));
        assert.deepEqual(saved.Sonarr.Instances, []);
        assert.deepEqual(saved.Radarr.Instances, []);
        assert.deepEqual(errors, []);
    }
    finally
    {
        await page.close();
    }
});

test('rejects missing keys and non-HTTP URLs before saving', async () =>
{
    const { page, errors } = await openConfig({});
    try
    {
        await page.locator('[data-arr-service=Sonarr]').click();
        const row = page.locator('#sonarrInstances .arrInstance');
        await row.locator('[data-field=Url]').fill('file:///invalid');
        await row.locator('[data-field=ApiKey]').fill('secret');
        await page.locator('#btnSaveConfigGlobal').click();
        assert.equal(await page.evaluate(() => window.savedConfigs.length), 0);
        await row.locator('[data-field=Url]').fill('http://valid.test');
        await row.locator('[data-field=ApiKey]').fill('');
        await page.locator('#btnSaveConfigGlobal').click();
        assert.equal(await page.evaluate(() => window.savedConfigs.length), 0);
        await row.locator('[data-field=ApiKey]').fill('valid-key');
        await page.locator('#btnSaveConfigGlobal').click();
        assert.equal(await page.evaluate(() => window.savedConfigs.length), 1);
        assert.deepEqual(errors, []);
    }
    finally
    {
        await page.close();
    }
});

test('reopening does not duplicate rows or add-button handlers', async () =>
{
    const { page, errors } = await openConfig({
        Sonarr: { Instances: [{ Name: 'Standard', Url: 'http://sonarr.test', ApiKey: 'secret' }] }
    });
    try
    {
        await page.evaluate(() =>
        {
            document.querySelector('#homeScreenSectionsConfigurationPage').dispatchEvent(new Event('pageshow'));
        });
        await page.waitForFunction(() => document.querySelectorAll('#sonarrInstances .arrInstance').length === 1);
        await page.locator('[data-arr-service=Sonarr]').click();
        assert.equal(await page.locator('#sonarrInstances .arrInstance').count(), 2);
        assert.deepEqual(errors, []);
    }
    finally
    {
        await page.close();
    }
});
