package ru.prokrust.escape.security;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.security.MessageDigest;
import java.util.Arrays;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** Android Keystore AES-GCM; encrypted records live in noBackupFilesDir. API 23+. */
public final class CredentialVault {
    private static final String ALIAS = "ru.prokrust.escape.credentials.aes.v1";
    private static final String TRANSFORM = "AES/GCM/NoPadding";
    private CredentialVault() {}

    private static File file(Context context, String key) throws Exception {
        byte[] digest = MessageDigest.getInstance("SHA-256").digest(key.getBytes(StandardCharsets.UTF_8));
        String name = Base64.encodeToString(digest, Base64.URL_SAFE | Base64.NO_WRAP | Base64.NO_PADDING);
        return new File(context.getNoBackupFilesDir(), "escape-credentials-v1-" + name);
    }

    private static SecretKey secret(Context context, boolean create) throws Exception {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        store.load(null);
        if (store.containsAlias(ALIAS)) return (SecretKey) store.getKey(ALIAS, null);
        if (!create) throw new SecurityException("Credential key unavailable");
        File[] existing = context.getNoBackupFilesDir().listFiles((dir, name) -> name.startsWith("escape-credentials-v1-"));
        if (existing == null || existing.length != 0) throw new SecurityException("Credential key unavailable");
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(ALIAS,
            KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setRandomizedEncryptionRequired(true).setKeySize(256).build());
        return generator.generateKey();
    }

    public static synchronized String read(Context context, String key) throws Exception {
        File target = file(context, key);
        if (!target.exists()) return null;
        long length = target.length();
        if (length < 30 || length > 1024 * 1024) throw new SecurityException("Invalid credential record");
        byte[] blob = new byte[(int) length];
        try (FileInputStream input = new FileInputStream(target)) {
            int offset = 0;
            while (offset < blob.length) {
                int count = input.read(blob, offset, blob.length - offset);
                if (count <= 0) throw new SecurityException("Incomplete credential record");
                offset += count;
            }
        }
        if (blob[0] != 1) throw new SecurityException("Invalid credential version");
        Cipher cipher = Cipher.getInstance(TRANSFORM);
        cipher.init(Cipher.DECRYPT_MODE, secret(context, false), new GCMParameterSpec(128, Arrays.copyOfRange(blob, 1, 13)));
        cipher.updateAAD(key.getBytes(StandardCharsets.UTF_8));
        byte[] plain = cipher.doFinal(blob, 13, blob.length - 13);
        try { return new String(plain, StandardCharsets.UTF_8); }
        finally { Arrays.fill(plain, (byte) 0); }
    }

    public static synchronized String write(Context context, String key, String value) throws Exception {
        File target = file(context, key);
        // Never replace a lost/invalidated key while encrypted records already exist.
        if (target.exists()) read(context, key);
        Cipher cipher = Cipher.getInstance(TRANSFORM);
        cipher.init(Cipher.ENCRYPT_MODE, secret(context, !target.exists()));
        cipher.updateAAD(key.getBytes(StandardCharsets.UTF_8));
        byte[] plain = value.getBytes(StandardCharsets.UTF_8);
        byte[] encrypted;
        try { encrypted = cipher.doFinal(plain); }
        finally { Arrays.fill(plain, (byte) 0); }
        File temporary = File.createTempFile("escape-credential-", ".tmp", context.getNoBackupFilesDir());
        try {
            try (FileOutputStream output = new FileOutputStream(temporary)) {
                output.write(1);
                output.write(cipher.getIV());
                output.write(encrypted);
                output.getFD().sync();
            }
            // rename(2) replaces atomically in the same filesystem, including an existing target.
            android.system.Os.rename(temporary.getAbsolutePath(), target.getAbsolutePath());
        } finally { temporary.delete(); }
        if (!value.equals(read(context, key))) throw new SecurityException("Credential verification failed");
        return "ok";
    }
}
