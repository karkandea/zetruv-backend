#!/usr/bin/env python3
import hashlib
import json
import os
import sys
import urllib.error
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, new_url):
        return None


def check_account(username, api_key):
    client = urllib.request.build_opener(NoRedirect())

    def request(endpoint, command, signing_suffix):
        signature = hashlib.md5((username + api_key + signing_suffix).encode()).hexdigest()
        body = json.dumps({"cmd": command, "username": username, "sign": signature}).encode()
        query = urllib.request.Request(
            "https://api.digiflazz.com/v1/" + endpoint,
            data=body,
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        try:
            with client.open(query, timeout=15) as response:
                return json.load(response)["data"]
        except urllib.error.HTTPError as exc:
            raise ValueError(f"{endpoint}: API returned HTTP {exc.code}.") from None
        except (urllib.error.URLError, TimeoutError, ValueError, KeyError):
            raise ValueError(f"{endpoint}: API response unavailable or invalid.") from None

    balance = request("cek-saldo", "deposit", "depo")
    if not isinstance(balance, dict) or not isinstance(balance.get("deposit"), (int, float)):
        raise ValueError("cek-saldo: Digiflazz did not return a Buyer deposit.")

    price_list = request("price-list", "prepaid", "pricelist")
    if not isinstance(price_list, list):
        raise ValueError("price-list: Digiflazz did not return the prepaid Buyer catalog.")

    fields = ("product_name", "buyer_sku_code", "seller_name", "price", "buyer_product_status",
              "seller_product_status", "unlimited_stock", "stock", "type")
    codm = [
        {field: row.get(field) for field in fields}
        for row in price_list
        if isinstance(row, dict) and any(
            "call of duty" in str(row.get(field, "")).lower()
            for field in ("brand", "product_name")
        )
    ]
    return {"deposit_idr": balance["deposit"], "codm_packages": codm}


if __name__ == "__main__":
    username = os.environ.get("DIGIFLAZZ_USERNAME", "").strip()
    api_key = os.environ.get("DIGIFLAZZ_API_KEY", "").strip()
    if not username or not api_key:
        sys.exit("Set DIGIFLAZZ_USERNAME and DIGIFLAZZ_API_KEY from the Buyer runtime secrets.")
    try:
        print(json.dumps(check_account(username, api_key), ensure_ascii=False, indent=2))
    except ValueError as error:
        sys.exit(str(error))
