/* eslint-disable @typescript-eslint/no-explicit-any */
import type { FC } from 'react'
import React, { useState } from 'react'
import { useMutation } from 'react-apollo'
import { FormattedMessage, useIntl } from 'react-intl'
import {
  Alert,
  Button,
  Card,
  Divider,
  IconHelp,
  Tooltip,
} from 'vtex.styleguide'

import M_ADD_IMAGES from '../mutations/AddImages.gql'

const AddImagesButton: FC = () => {
  const intl = useIntl()
  const [showAlert, setShowAlert] = useState(true)
  const [addImages, { loading: addingImages, data: imagesAdded }] =
    useMutation(M_ADD_IMAGES)

  const displayAlert = !addingImages && imagesAdded && showAlert

  return (
    <div>
      {displayAlert && (
        <div className="mb2">
          <Alert
            type="success"
            onClose={() => setShowAlert(false)}
            autoClose={6000}
          >
            <FormattedMessage id="admin/sheets-catalog-import.add-images.success" />
          </Alert>
        </div>
      )}
      <Card>
        <div className="flex">
          <div className="w-70">
            <div>
              <span className="mr5">
                <FormattedMessage id="admin/sheets-catalog-import.add-images.description" />
              </span>
              <Tooltip
                label={intl.formatMessage({
                  id: 'admin/sheets-catalog-import.add-images.tooltip',
                  defaultMessage:
                    'This feature only works if you are also using the VTEX Google Drive App, which creates a folder in your drive. You can use the folder titled NEW to add product images. These images will be prepopulated in your Google Catalog Import spreadsheet.',
                })}
              >
                <span className="ml-4 c-on-base pointer">
                  <IconHelp />
                </span>
              </Tooltip>
            </div>
          </div>
          <div
            style={{ flexGrow: 1 }}
            className="flex items-stretch w-20 justify-center"
          >
            <Divider orientation="vertical" />
          </div>
          <div className="w-30 items-center flex">
            {!imagesAdded?.addImages && (
              <Button
                variation="secondary"
                collapseLeft
                block
                isLoading={addingImages}
                onClick={() => {
                  addImages()
                  setShowAlert(true)
                }}
              >
                <FormattedMessage id="admin/sheets-catalog-import.add-images.button" />
              </Button>
            )}
            {!addingImages && imagesAdded?.addImages && (
              <p>
                <strong>{`${imagesAdded.addImages}`}</strong>
              </p>
            )}
          </div>
        </div>
      </Card>
      <br />
    </div>
  )
}

export default AddImagesButton
