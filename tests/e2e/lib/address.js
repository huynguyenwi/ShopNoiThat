// Shared by the E2E suites: picks a province, waits for its wards (loaded from /api/locations) and picks one.
async function chooseAddress(page, province, ward) {
  const provinceValue = province || await page.$$eval('#Command_Province option', o => o.map(x => x.value).filter(Boolean)[0]);
  await page.select('#Command_Province', provinceValue);
  await page.waitForFunction(p => {
    const select = document.getElementById('Command_Ward');
    return select && !select.disabled && document.getElementById('Command_Province').value === p
      && [...select.options].some(o => o.value);
  }, { timeout: 10000 }, provinceValue);
  const wardValue = ward || await page.$$eval('#Command_Ward option', o => o.map(x => x.value).filter(Boolean)[0]);
  await page.select('#Command_Ward', wardValue);
  return { province: provinceValue, ward: wardValue };
}

module.exports = { chooseAddress };
