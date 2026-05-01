const LISTING_URL = "{{ listingInfo.Url }}";

const copyText = async (value) => {
  try {
    await navigator.clipboard.writeText(value);
  } catch {
    const fallback = document.createElement('textarea');
    fallback.value = value;
    fallback.setAttribute('readonly', 'readonly');
    fallback.style.position = 'absolute';
    fallback.style.left = '-9999px';
    document.body.appendChild(fallback);
    fallback.select();
    document.execCommand('copy');
    document.body.removeChild(fallback);
  }
};

const attachSearch = () => {
  const searchInput = document.getElementById('packageSearch');
  const cards = Array.from(document.querySelectorAll('.package-card'));

  searchInput?.addEventListener('input', (event) => {
    const value = String(event.target.value ?? '').trim().toLowerCase();
    cards.forEach((card) => {
      const haystack = String(card.dataset.search ?? '').toLowerCase();
      card.hidden = value.length > 0 && !haystack.includes(value);
    });
  });
};

const attachListingActions = () => {
  const listingUrl = document.getElementById('listingUrl');
  document.getElementById('copyListingUrl')?.addEventListener('click', () => {
    if (!listingUrl) {
      return;
    }

    copyText(listingUrl.value);
  });

  document.getElementById('addListingToVcc')?.addEventListener('click', () => {
    window.location.assign(`vcc://vpm/addRepo?url=${encodeURIComponent(LISTING_URL)}`);
  });
};

const attachPackageActions = () => {
  document.querySelectorAll('.copy-package-id').forEach((button) => {
    button.addEventListener('click', () => {
      const packageId = button.dataset.packageId ?? '';
      if (packageId) {
        copyText(packageId);
      }
    });
  });
};

attachSearch();
attachListingActions();
attachPackageActions();